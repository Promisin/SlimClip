using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacHotkeyService : IHotkeyService
{
    public event EventHandler? HotkeyPressed;
#pragma warning disable CS0067, CS0414
    public event EventHandler<HotkeyConfig>? HotkeyRecorded;

    private HotkeyConfig? _currentConfig;
    private IntPtr _carbonHandlerRef = IntPtr.Zero;
    private IntPtr _carbonHotKeyRef = IntPtr.Zero;
    private MacNativeMethods.EventHandlerDelegate? _carbonHandlerDelegate;

    private IntPtr _eventTap = IntPtr.Zero;
    private IntPtr _runLoopSource = IntPtr.Zero;
    private MacNativeMethods.CGEventTapCallback? _callback;
    private bool _isRecording;
#pragma warning restore CS0067, CS0414
    private bool _isDisposed;
    private long _lastTriggerTick;

    public void StartRecording()
    {
        _isRecording = true;
    }

    public void StopRecording()
    {
        _isRecording = false;
    }

    public bool Register(HotkeyConfig config)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;

        Unregister();
        _currentConfig = config;

        if (!config.IsEnabled)
            return true;

        bool carbonSuccess = RegisterCarbonHotKey(config);
        bool eventTapSuccess = RegisterEventTap(config);

        return carbonSuccess || eventTapSuccess;
    }

    private bool RegisterCarbonHotKey(HotkeyConfig config)
    {
        try
        {
            // 1. Install application-wide Carbon event handler if not already installed
            if (_carbonHandlerRef == IntPtr.Zero)
            {
                _carbonHandlerDelegate = OnCarbonHotKeyEvent;
                var spec = new[]
                {
                    new MacNativeMethods.EventTypeSpec
                    {
                        eventClass = MacNativeMethods.kEventClassKeyboard,
                        eventKind = MacNativeMethods.kEventHotKeyPressed
                    }
                };

                var target = MacNativeMethods.GetApplicationEventTarget();
                if (target != IntPtr.Zero)
                {
                    MacNativeMethods.InstallEventHandler(
                        target,
                        _carbonHandlerDelegate,
                        1,
                        spec,
                        IntPtr.Zero,
                        out _carbonHandlerRef);
                }
            }

            // 2. Convert modifiers to Carbon mask
            uint carbonMods = 0;
            if (config.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand)) carbonMods |= MacNativeMethods.carbonCmdKey;
            if (config.Modifiers.HasFlag(KeyModifiers.Shift)) carbonMods |= MacNativeMethods.carbonShiftKey;
            if (config.Modifiers.HasFlag(KeyModifiers.Alt)) carbonMods |= MacNativeMethods.carbonOptionKey;
            if (config.Modifiers.HasFlag(KeyModifiers.Control)) carbonMods |= MacNativeMethods.carbonControlKey;

            var hkId = new MacNativeMethods.EventHotKeyID
            {
                signature = 0x73636c70, // 'sclp'
                id = 1
            };

            var appTarget = MacNativeMethods.GetApplicationEventTarget();
            if (appTarget != IntPtr.Zero)
            {
                int status = MacNativeMethods.RegisterEventHotKey(
                    (uint)config.KeyCode,
                    carbonMods,
                    hkId,
                    appTarget,
                    0,
                    out _carbonHotKeyRef);

                if (status == 0 && _carbonHotKeyRef != IntPtr.Zero)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private int OnCarbonHotKeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try
        {
            var hkId = new MacNativeMethods.EventHotKeyID();
            int status = MacNativeMethods.GetEventParameter(
                theEvent,
                MacNativeMethods.kEventParamDirectObject,
                MacNativeMethods.typeEventHotKeyID,
                IntPtr.Zero,
                (uint)Marshal.SizeOf<MacNativeMethods.EventHotKeyID>(),
                out _,
                ref hkId);

            if (status == 0 && hkId.id == 1 && _currentConfig != null && _currentConfig.IsEnabled)
            {
                TriggerHotkey();
                return 0; // noErr
            }
        }
        catch { }

        return 0;
    }

    private bool RegisterEventTap(HotkeyConfig config)
    {
        try
        {
            _callback = EventTapCallback;
            ulong eventMask = 1UL << (int)MacNativeMethods.kCGEventKeyDown;

            _eventTap = MacNativeMethods.CGEventTapCreate(
                MacNativeMethods.kCGHIDEventTap,
                0, // kCGHeadInsertEventTap
                0, // kCGEventTapOptionDefault
                eventMask,
                _callback,
                IntPtr.Zero);

            if (_eventTap != IntPtr.Zero)
            {
                _runLoopSource = MacNativeMethods.CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, IntPtr.Zero);
                if (_runLoopSource != IntPtr.Zero)
                {
                    var rl = MacNativeMethods.CFRunLoopGetMain();
                    var commonModes = MacNativeMethods.GetCFRunLoopCommonModes();
                    if (commonModes != IntPtr.Zero)
                    {
                        MacNativeMethods.CFRunLoopAddSource(rl, _runLoopSource, commonModes);
                    }
                    MacNativeMethods.CGEventTapEnable(_eventTap, true);
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public void Unregister()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        try
        {
            if (_carbonHotKeyRef != IntPtr.Zero)
            {
                MacNativeMethods.UnregisterEventHotKey(_carbonHotKeyRef);
                _carbonHotKeyRef = IntPtr.Zero;
            }

            if (_eventTap != IntPtr.Zero)
            {
                MacNativeMethods.CGEventTapEnable(_eventTap, false);
                if (_runLoopSource != IntPtr.Zero)
                {
                    var rl = MacNativeMethods.CFRunLoopGetMain();
                    var commonModes = MacNativeMethods.GetCFRunLoopCommonModes();
                    if (commonModes != IntPtr.Zero)
                    {
                        MacNativeMethods.CFRunLoopRemoveSource(rl, _runLoopSource, commonModes);
                    }
                    MacNativeMethods.CFRelease(_runLoopSource);
                    _runLoopSource = IntPtr.Zero;
                }
                MacNativeMethods.CFRelease(_eventTap);
                _eventTap = IntPtr.Zero;
            }
        }
        catch { }
    }

    private IntPtr EventTapCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr refcon)
    {
        // Re-enable if disabled by timeout or user input
        if (type == 0xFFFFFFFE || type == 0xFFFFFFFF)
        {
            if (_eventTap != IntPtr.Zero)
            {
                MacNativeMethods.CGEventTapEnable(_eventTap, true);
            }
            return @event;
        }

        if (type == MacNativeMethods.kCGEventKeyDown && _currentConfig != null && _currentConfig.IsEnabled)
        {
            var keycode = MacNativeMethods.CGEventGetIntegerValueField(@event, MacNativeMethods.kCGKeyboardEventKeycode);
            var flags = MacNativeMethods.CGEventGetFlags(@event);

            bool cmd = (flags & MacNativeMethods.kCGEventFlagMaskCommand) != 0;
            bool shift = (flags & MacNativeMethods.kCGEventFlagMaskShift) != 0;
            bool alt = (flags & MacNativeMethods.kCGEventFlagMaskAlternate) != 0;
            bool ctrl = (flags & MacNativeMethods.kCGEventFlagMaskControl) != 0;

            bool matchCmd = _currentConfig.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand) == cmd;
            bool matchShift = _currentConfig.Modifiers.HasFlag(KeyModifiers.Shift) == shift;
            bool matchAlt = _currentConfig.Modifiers.HasFlag(KeyModifiers.Alt) == alt;
            bool matchCtrl = _currentConfig.Modifiers.HasFlag(KeyModifiers.Control) == ctrl;

            if (matchCmd && matchShift && matchAlt && matchCtrl && keycode == _currentConfig.KeyCode)
            {
                TriggerHotkey();
                return IntPtr.Zero; // Swallow hotkey
            }
        }

        return @event;
    }

    public void TriggerHotkey()
    {
        var now = Environment.TickCount64;
        if (now - _lastTriggerTick < 250)
            return; // Debounce rapid duplicate triggers
        _lastTriggerTick = now;

        HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Unregister();

        if (_carbonHandlerRef != IntPtr.Zero)
        {
            try
            {
                MacNativeMethods.RemoveEventHandler(_carbonHandlerRef);
            }
            catch { }
            _carbonHandlerRef = IntPtr.Zero;
        }
    }
}
