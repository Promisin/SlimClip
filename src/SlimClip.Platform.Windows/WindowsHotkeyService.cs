using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsHotkeyService : IHotkeyService
{
    public event EventHandler? HotkeyPressed;
    public event EventHandler<HotkeyConfig>? HotkeyRecorded;

    private const int HOTKEY_ID = 9001;
    private const string ExplorerAdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DisabledHotkeysValueName = "DisabledHotkeys";

    private IntPtr _messageHwnd = IntPtr.Zero;
    private readonly NativeMethods.WndProcDelegate _wndProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;

    private HotkeyConfig? _currentConfig;
    private bool _isRecording;
    private bool _isDisposed;

    private bool _allowSystemOverride;
    private string _managedDisabledChar = string.Empty;
    private Action<string>? _onManagedCharChanged;

    // 录制期间的修饰键状态（仅在 UI 线程读写）
    private bool _isWinDown;
    private bool _isCtrlDown;
    private bool _isAltDown;
    private bool _isShiftDown;

    public HotkeyRegistrationStatus LastRegistrationStatus { get; private set; } = HotkeyRegistrationStatus.Success;

    internal bool IsHookInstalled => _hookHandle != IntPtr.Zero;

    /// <summary>原生 RegisterHotKey 调用接缝（便于单元测试隔离系统状态）。</summary>
    internal Func<IntPtr, int, uint, uint, bool>? NativeRegisterHotKeyOverride { get; set; }

    /// <summary>原生 UnregisterHotKey 调用接缝。</summary>
    internal Func<IntPtr, int, bool>? NativeUnregisterHotKeyOverride { get; set; }

    /// <summary>读取 Explorer DisabledHotkeys 注册表值的接缝。</summary>
    internal Func<string?>? ReadDisabledHotkeysOverride { get; set; }

    /// <summary>写入 Explorer DisabledHotkeys 注册表值的接缝（传 null 或空串表示删除该值）。</summary>
    internal Action<string?>? WriteDisabledHotkeysOverride { get; set; }

    public WindowsHotkeyService()
    {
        _wndProc = WndProc;
        _hookProc = LowLevelKeyboardHook;
        CreateMessageWindow();
    }

    private void CreateMessageWindow()
    {
        var className = "SlimClip_HotkeyWin_" + Guid.NewGuid().ToString("N");
        var wndClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = _wndProc,
            lpszClassName = className,
            hInstance = NativeMethods.GetModuleHandleW(null)
        };

        NativeMethods.RegisterClassExW(ref wndClass);

        _messageHwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            "SlimClipHotkeyWindow",
            0,
            0, 0, 0, 0,
            (IntPtr)NativeMethods.HWND_MESSAGE,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);
    }

    public void ConfigureSystemHotkeyOverride(bool allowOverride, string? initialManagedChar = null, Action<string>? onManagedCharChanged = null)
    {
        if (initialManagedChar != null)
        {
            _managedDisabledChar = initialManagedChar;
        }
        if (onManagedCharChanged != null)
        {
            _onManagedCharChanged = onManagedCharChanged;
        }

        _allowSystemOverride = allowOverride;

        if (!_allowSystemOverride)
        {
            RemoveManagedDisabledHotkeyChar();
        }
    }

    public bool Register(HotkeyConfig config)
    {
        UnregisterInternal();
        _currentConfig = config;

        if (!config.IsEnabled)
        {
            RemoveManagedDisabledHotkeyChar();
            LastRegistrationStatus = HotkeyRegistrationStatus.Success;
            return true;
        }

        uint fsModifiers = (uint)NativeMethods.MOD_NOREPEAT;
        if (config.Modifiers.HasFlag(KeyModifiers.Alt)) fsModifiers |= (uint)NativeMethods.MOD_ALT;
        if (config.Modifiers.HasFlag(KeyModifiers.Control)) fsModifiers |= (uint)NativeMethods.MOD_CONTROL;
        if (config.Modifiers.HasFlag(KeyModifiers.Shift)) fsModifiers |= (uint)NativeMethods.MOD_SHIFT;
        if (config.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand)) fsModifiers |= (uint)NativeMethods.MOD_WIN;

        bool hasWin = config.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand);
        char? overrideChar = TryGetDisabledHotkeyChar(config.KeyCode);

        // 若新快捷键不需要禁用之前的字符，先清理旧的托管字符
        if (!_allowSystemOverride || !hasWin || overrideChar == null || _managedDisabledChar != overrideChar.Value.ToString())
        {
            RemoveManagedDisabledHotkeyChar();
        }

        bool registered = InvokeRegisterHotKey(_messageHwnd, HOTKEY_ID, fsModifiers, (uint)config.KeyCode);
        if (registered)
        {
            LastRegistrationStatus = HotkeyRegistrationStatus.Success;
            return true;
        }

        // 原生 RegisterHotKey 失败：不挂载后台钩子，立即暴露冲突状态并按模式处理注册表
        if (hasWin && overrideChar.HasValue)
        {
            if (_allowSystemOverride)
            {
                EnsureManagedDisabledHotkeyChar(overrideChar.Value);
                LastRegistrationStatus = HotkeyRegistrationStatus.PendingExplorerRestart;
            }
            else
            {
                LastRegistrationStatus = HotkeyRegistrationStatus.OccupiedCanOverrideSystem;
            }
        }
        else
        {
            LastRegistrationStatus = HotkeyRegistrationStatus.OccupiedCannotRegister;
        }

        return false;
    }

    public bool RestartExplorerAndReRegister()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            Thread.Sleep(600);
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }

            // 等待新 Explorer 进程完成初始化并读取 DisabledHotkeys
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(350);
                if (_currentConfig != null && Register(_currentConfig))
                {
                    return true;
                }
            }
        }
        catch { }

        return _currentConfig != null && Register(_currentConfig);
    }

    private static char? TryGetDisabledHotkeyChar(int vkCode)
    {
        if (vkCode is >= 0x41 and <= 0x5A) // A - Z
            return (char)vkCode;
        if (vkCode is >= 0x30 and <= 0x39) // 0 - 9
            return (char)vkCode;
        return null;
    }

    private void EnsureManagedDisabledHotkeyChar(char targetChar)
    {
        string targetStr = char.ToUpperInvariant(targetChar).ToString();
        string current = ReadDisabledHotkeys() ?? string.Empty;

        if (!current.Contains(targetStr, StringComparison.OrdinalIgnoreCase))
        {
            WriteDisabledHotkeys(current + targetStr);
        }

        if (_managedDisabledChar != targetStr)
        {
            _managedDisabledChar = targetStr;
            _onManagedCharChanged?.Invoke(_managedDisabledChar);
        }
    }

    private void RemoveManagedDisabledHotkeyChar()
    {
        if (string.IsNullOrEmpty(_managedDisabledChar))
            return;

        string current = ReadDisabledHotkeys() ?? string.Empty;
        if (!string.IsNullOrEmpty(current))
        {
            string updated = current.Replace(_managedDisabledChar, string.Empty, StringComparison.OrdinalIgnoreCase);
            if (updated != current)
            {
                WriteDisabledHotkeys(string.IsNullOrEmpty(updated) ? null : updated);
            }
        }

        _managedDisabledChar = string.Empty;
        _onManagedCharChanged?.Invoke(string.Empty);
    }

    private string? ReadDisabledHotkeys()
    {
        if (ReadDisabledHotkeysOverride != null)
            return ReadDisabledHotkeysOverride();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKeyPath, writable: false);
            return key?.GetValue(DisabledHotkeysValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private void WriteDisabledHotkeys(string? value)
    {
        if (WriteDisabledHotkeysOverride != null)
        {
            WriteDisabledHotkeysOverride(value);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKeyPath, writable: true);
            if (key == null) return;

            if (string.IsNullOrEmpty(value))
            {
                key.DeleteValue(DisabledHotkeysValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(DisabledHotkeysValueName, value, RegistryValueKind.String);
            }
        }
        catch { }
    }

    private bool InvokeRegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk)
    {
        if (NativeRegisterHotKeyOverride != null)
            return NativeRegisterHotKeyOverride(hWnd, id, fsModifiers, vk);
        return NativeMethods.RegisterHotKey(hWnd, id, fsModifiers, vk);
    }

    private void InvokeUnregisterHotKey(IntPtr hWnd, int id)
    {
        if (NativeUnregisterHotKeyOverride != null)
        {
            NativeUnregisterHotKeyOverride(hWnd, id);
            return;
        }
        NativeMethods.UnregisterHotKey(hWnd, id);
    }

    public void Unregister()
    {
        UnregisterInternal();
    }

    private void UnregisterInternal()
    {
        if (_messageHwnd != IntPtr.Zero)
        {
            InvokeUnregisterHotKey(_messageHwnd, HOTKEY_ID);
        }
        if (!_isRecording)
        {
            UninstallHook();
        }
    }

    public void StartRecording()
    {
        _isRecording = true;
        ResetModifierStates();
        InstallHook();
    }

    public void StopRecording()
    {
        _isRecording = false;
        ResetModifierStates();
        UninstallHook();
    }

    private void ResetModifierStates()
    {
        _isWinDown = false;
        _isCtrlDown = false;
        _isAltDown = false;
        _isShiftDown = false;
    }

    private void InstallHook()
    {
        if (_hookHandle == IntPtr.Zero && NativeRegisterHotKeyOverride == null)
        {
            var hMod = NativeMethods.GetModuleHandleW(null);
            _hookHandle = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        }
    }

    private void UninstallHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private IntPtr LowLevelKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isInjected = (kbd.flags & NativeMethods.LLKHF_INJECTED) != 0;

            var result = ProcessKeyEvent(msg, kbd.vkCode, kbd.flags, isInjected);
            if (result != IntPtr.Zero)
            {
                return result;
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    internal IntPtr ProcessKeyEvent(int msg, uint vk, uint flags, bool isInjected)
    {
        if (!_isRecording)
            return IntPtr.Zero;

        bool isKeyDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
        bool isKeyUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

        if (!isKeyDown && !isKeyUp)
            return IntPtr.Zero;

        if (!isInjected)
        {
            if (vk is 0x5B or 0x5C)
                _isWinDown = isKeyDown;
            else if (vk is 0x11 or 0xA2 or 0xA3)
                _isCtrlDown = isKeyDown;
            else if (vk is 0x12 or 0xA4 or 0xA5)
                _isAltDown = isKeyDown;
            else if (vk is 0x10 or 0xA0 or 0xA1)
                _isShiftDown = isKeyDown;
        }

        if (!isKeyDown)
            return IntPtr.Zero;

        // 单独按下修饰键时继续等待主键
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
        {
            return IntPtr.Zero;
        }

        if (vk == 0x1B) // VK_ESCAPE
        {
            StopRecording();
            return (IntPtr)1;
        }

        bool alt = _isAltDown || ((flags & NativeMethods.LLKHF_ALTDOWN) != 0);
        bool ctrl = _isCtrlDown;
        bool shift = _isShiftDown;
        bool win = _isWinDown;

        var mods = KeyModifiers.None;
        if (alt) mods |= KeyModifiers.Alt;
        if (ctrl) mods |= KeyModifiers.Control;
        if (shift) mods |= KeyModifiers.Shift;
        if (win) mods |= KeyModifiers.WindowsOrCommand;

        bool isFunctionKey = vk >= 0x70 && vk <= 0x87; // F1 - F24

        if (mods == KeyModifiers.None)
        {
            if (vk == 0x08) // Backspace 清空快捷键
                return IntPtr.Zero;

            if (!isFunctionKey)
                return (IntPtr)1; // 拒绝无修饰键的单字母，保持录制状态
        }

        // 若组合键包含 Win 键，且处于真实系统钩子环境，注入一次 Ctrl 脉冲以中和 Windows 开始菜单
        if (win && _hookHandle != IntPtr.Zero)
        {
            NeutralizeWinKeyStartMenu();
        }

        var display = FormatWinDisplay(mods, vk);
        var recordedConfig = new HotkeyConfig
        {
            Modifiers = mods,
            KeyCode = (int)vk,
            DisplayString = display,
            IsEnabled = true
        };

        StopRecording();
        HotkeyRecorded?.Invoke(this, recordedConfig);
        return (IntPtr)1;
    }

    private static void NeutralizeWinKeyStartMenu()
    {
        try
        {
            var inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = NativeMethods.VK_CONTROL;
            inputs[0].u.ki.wScan = 0x1D;

            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = NativeMethods.VK_CONTROL;
            inputs[1].u.ki.wScan = 0x1D;
            inputs[1].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
        catch { }
    }

    private static string FormatWinDisplay(KeyModifiers mods, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(KeyModifiers.WindowsOrCommand)) parts.Add("Win");
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");

        string keyName = vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
            _ => $"Key{vk}"
        };

        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopRecording();
        UnregisterInternal();
        if (_messageHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_messageHwnd);
            _messageHwnd = IntPtr.Zero;
        }
    }
}
