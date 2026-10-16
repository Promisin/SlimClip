using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsHotkeyService : IHotkeyService
{
    public event EventHandler? HotkeyPressed;
    public event EventHandler<HotkeyConfig>? HotkeyRecorded;

    private const int HOTKEY_ID = 9001;
    private IntPtr _messageHwnd = IntPtr.Zero;
    private readonly NativeMethods.WndProcDelegate _wndProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;

    private HotkeyConfig? _currentConfig;
    private bool _isRecording;
    private bool _isDisposed;

    private volatile bool _isWinDown;
    private volatile bool _isCtrlDown;
    private volatile bool _isAltDown;
    private volatile bool _isShiftDown;
    private volatile bool _isChordTriggered;

    public WindowsHotkeyService()
    {
        _wndProc = WndProc;
        _hookProc = LowLevelKeyboardHook;
        CreateMessageWindow();
        PreWarmNeutralizer();
    }

    private void PreWarmNeutralizer()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(
                typeof(WindowsHotkeyService).GetMethod(nameof(NeutralizeWinKey), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.MethodHandle);
        }
        catch { }
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

    public bool Register(HotkeyConfig config)
    {
        Unregister();
        _currentConfig = config;

        if (!config.IsEnabled)
            return true;

        uint fsModifiers = 0;
        if (config.Modifiers.HasFlag(KeyModifiers.Alt)) fsModifiers |= (uint)NativeMethods.MOD_ALT;
        if (config.Modifiers.HasFlag(KeyModifiers.Control)) fsModifiers |= (uint)NativeMethods.MOD_CONTROL;
        if (config.Modifiers.HasFlag(KeyModifiers.Shift)) fsModifiers |= (uint)NativeMethods.MOD_SHIFT;
        if (config.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand)) fsModifiers |= (uint)NativeMethods.MOD_WIN;

        // If Windows key is involved, install LL hook to intercept without opening Start menu
        if (config.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand))
        {
            InstallHook();
            return true;
        }

        // Otherwise use RegisterHotKey
        fsModifiers |= (uint)NativeMethods.MOD_NOREPEAT;
        var success = NativeMethods.RegisterHotKey(_messageHwnd, HOTKEY_ID, fsModifiers, (uint)config.KeyCode);
        if (!success)
        {
            // Fallback to hook
            InstallHook();
        }

        return true;
    }

    public void Unregister()
    {
        if (_messageHwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_messageHwnd, HOTKEY_ID);
        }
        if (!_isRecording && !IsWinKeySuppressionActive)
        {
            UninstallHook();
        }
        IsWinKeySuppressionActive = false;
        SuppressedSuffixVk = 0;
        _isChordTriggered = false;
    }

    public void StartRecording()
    {
        _isRecording = true;
        SyncModifierStates();
        InstallHook();
    }

    public void StopRecording()
    {
        _isRecording = false;
        SyncModifierStates();
        // If current config doesn't require a hook and no Win key suppression is active, uninstall it
        if (_currentConfig == null || !_currentConfig.IsEnabled || !_currentConfig.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand))
        {
            if (!IsWinKeySuppressionActive && !_isWinDown)
            {
                UninstallHook();
            }
        }
    }

    private void SyncModifierStates()
    {
        _isWinDown = IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C);
        _isCtrlDown = IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3);
        _isAltDown = IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5);
        _isShiftDown = IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1);
    }

    private static bool IsPhysicalKeyDown(int vk)
    {
        return (NativeMethods.GetKeyState(vk) < 0) || ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0);
    }

    private void InstallHook()
    {
        if (_hookHandle == IntPtr.Zero)
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

    internal bool IsWinKeySuppressionActive { get; set; }
    internal uint SuppressedSuffixVk { get; set; }
    internal Action? OnWinKeyNeutralized { get; set; }

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
        bool isKeyDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
        bool isKeyUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

        if (isKeyDown || isKeyUp)
        {
            // Update physical modifier states from the input stream (ignore synthetic injected events)
            if (!isInjected)
            {
                if (vk is 0x5B or 0x5C)
                {
                    _isWinDown = isKeyDown;
                }
                else if (vk is 0x11 or 0xA2 or 0xA3)
                {
                    _isCtrlDown = isKeyDown;
                }
                else if (vk is 0x12 or 0xA4 or 0xA5)
                {
                    _isAltDown = isKeyDown;
                }
                else if (vk is 0x10 or 0xA0 or 0xA1)
                {
                    _isShiftDown = isKeyDown;
                }
            }

            if (isKeyUp)
            {
                // 1. Swallow keyup for hotkey suffix key (e.g. V up) so active app doesn't receive orphan release
                if (SuppressedSuffixVk != 0 && vk == SuppressedSuffixVk)
                {
                    SuppressedSuffixVk = 0;
                    _isChordTriggered = false;
                    return (IntPtr)1;
                }

                if (_currentConfig != null && vk == (uint)_currentConfig.KeyCode)
                {
                    _isChordTriggered = false;
                }

                // 2. Win key release: reset suppression state and ALWAYS let OS receive Win keyup!
                if (vk is 0x5B or 0x5C)
                {
                    IsWinKeySuppressionActive = false;
                }
            }

            if (isKeyDown)
            {
                // Compute effective modifiers with multiple redundancy checks
                bool alt = _isAltDown || ((flags & NativeMethods.LLKHF_ALTDOWN) != 0) ||
                           IsPhysicalKeyDown(0x12) || IsPhysicalKeyDown(0xA4) || IsPhysicalKeyDown(0xA5);
                bool ctrl = _isCtrlDown || IsPhysicalKeyDown(0x11) || IsPhysicalKeyDown(0xA2) || IsPhysicalKeyDown(0xA3);
                bool shift = _isShiftDown || IsPhysicalKeyDown(0x10) || IsPhysicalKeyDown(0xA0) || IsPhysicalKeyDown(0xA1);
                bool win = _isWinDown || IsPhysicalKeyDown(0x5B) || IsPhysicalKeyDown(0x5C);

                if (_isRecording)
                {
                    // Ignore isolated modifier keys alone (wait for key chord)
                    if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
                    {
                        return IntPtr.Zero;
                    }

                    if (vk == 0x1B) // VK_ESCAPE
                    {
                        StopRecording();
                        return (IntPtr)1; // Swallow ESC
                    }

                    var mods = KeyModifiers.None;
                    if (alt) mods |= KeyModifiers.Alt;
                    if (ctrl) mods |= KeyModifiers.Control;
                    if (shift) mods |= KeyModifiers.Shift;
                    if (win) mods |= KeyModifiers.WindowsOrCommand;

                    bool isFunctionKey = vk >= 0x70 && vk <= 0x87; // F1 - F24

                    if (mods == KeyModifiers.None)
                    {
                        if (vk == 0x08) // Backspace
                        {
                            // Pass through to window to clear hotkey
                            return IntPtr.Zero;
                        }

                        if (!isFunctionKey)
                        {
                            // Reject bare keys without modifiers as global hotkeys
                            return (IntPtr)1; // Swallow bare letter to avoid typing into background, keep recording
                        }
                    }

                    if (win)
                    {
                        IsWinKeySuppressionActive = true;
                        SuppressedSuffixVk = vk;
                        NeutralizeWinKey();
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
                    return (IntPtr)1; // SWALLOW key to prevent OS Shell (e.g. Win+V clipboard) from receiving it!
                }

                if (_currentConfig != null && _currentConfig.IsEnabled)
                {
                    bool matchAlt = _currentConfig.Modifiers.HasFlag(KeyModifiers.Alt) == alt;
                    bool matchCtrl = _currentConfig.Modifiers.HasFlag(KeyModifiers.Control) == ctrl;
                    bool matchShift = _currentConfig.Modifiers.HasFlag(KeyModifiers.Shift) == shift;
                    bool matchWin = _currentConfig.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand) == win;

                    if (matchAlt && matchCtrl && matchShift && matchWin && vk == (uint)_currentConfig.KeyCode)
                    {
                        // Neutralize Win key menu popup if Win was pressed
                        if (win)
                        {
                            IsWinKeySuppressionActive = true;
                            SuppressedSuffixVk = vk;
                            NeutralizeWinKey();
                        }

                        // Prevent hardware auto-repeat from repeatedly firing hotkey event and causing UI jitter/dismiss
                        if (!_isChordTriggered)
                        {
                            _isChordTriggered = true;
                            HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        }

                        return (IntPtr)1; // Swallow key
                    }
                }
            }
        }

        return IntPtr.Zero;
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

    private void NeutralizeWinKey()
    {
        OnWinKeyNeutralized?.Invoke();

        // Inject synthetic VK_CONTROL (0x11 with scancode 0x1D) to neutralize Start menu
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

            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            if (sent == 0)
            {
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0x1D, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0x1D, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }
        catch
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0x1D, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0x1D, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Unregister();
        if (_messageHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_messageHwnd);
            _messageHwnd = IntPtr.Zero;
        }
    }
}
