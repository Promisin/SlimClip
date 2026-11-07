using System;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.Platform.Windows;
using Xunit;

namespace SlimClip.Tests;

public class WindowsHotkeyServiceTests
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_V = 86;
    private const uint VK_E = 69;

    [Fact]
    public void Register_WhenNativeRegisterHotKeySucceeds_ReturnsTrueAndStatusSuccess_WithoutHook()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: true, out _);

        bool ok = service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.Alt | KeyModifiers.Shift,
            KeyCode = (int)VK_V,
            DisplayString = "Alt + Shift + V",
            IsEnabled = true
        });

        Assert.True(ok);
        Assert.Equal(HotkeyRegistrationStatus.Success, service.LastRegistrationStatus);
        Assert.False(service.IsHookInstalled, "Runtime hotkey must NOT install WH_KEYBOARD_LL hook");
    }

    [Fact]
    public void Register_WinV_WhenOccupiedAndOverrideDisabled_ReturnsOccupiedCanOverrideSystem_AndDoesNotTouchRegistry()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: false, out var regStore);

        bool ok = service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        Assert.False(ok);
        Assert.Equal(HotkeyRegistrationStatus.OccupiedCanOverrideSystem, service.LastRegistrationStatus);
        Assert.Null(regStore.Value);
        Assert.False(service.IsHookInstalled, "Failed registration must NOT fall back to runtime WH_KEYBOARD_LL hook");
    }

    [Fact]
    public void Register_WinV_WhenOccupiedAndOverrideEnabled_IncrementallyWritesDisabledHotkeys_AndPreservesUserChars()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: false, out var regStore);
        regStore.Value = "F"; // User already disabled Win+F externally
        string persistedManagedChar = string.Empty;

        service.ConfigureSystemHotkeyOverride(
            allowOverride: true,
            initialManagedChar: string.Empty,
            onManagedCharChanged: c => persistedManagedChar = c);

        // 1. Register Win + V -> appends 'V', keeps 'F'
        bool ok = service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        Assert.False(ok);
        Assert.Equal(HotkeyRegistrationStatus.PendingExplorerRestart, service.LastRegistrationStatus);
        Assert.Equal("FV", regStore.Value);
        Assert.Equal("V", persistedManagedChar);

        // 2. Switch to Win + E -> removes 'V', appends 'E', still keeps 'F'
        service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_E,
            DisplayString = "Win + E",
            IsEnabled = true
        });

        Assert.Equal("FE", regStore.Value);
        Assert.Equal("E", persistedManagedChar);

        // 3. Turn off override mode -> removes 'E', keeps 'F' intact
        service.ConfigureSystemHotkeyOverride(
            allowOverride: false,
            initialManagedChar: persistedManagedChar,
            onManagedCharChanged: c => persistedManagedChar = c);

        Assert.Equal("F", regStore.Value);
        Assert.Equal(string.Empty, persistedManagedChar);
    }

    [Fact]
    public void Register_WinNonAlphanumeric_WhenOccupied_ReturnsOccupiedCannotRegister_EvenIfOverrideEnabled()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: false, out var regStore);
        service.ConfigureSystemHotkeyOverride(allowOverride: true);

        // VK_OEM_1 (';' = 0xBA) is not an ASCII letter/digit supported by DisabledHotkeys
        bool ok = service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = 0xBA,
            DisplayString = "Win + ;",
            IsEnabled = true
        });

        Assert.False(ok);
        Assert.Equal(HotkeyRegistrationStatus.OccupiedCannotRegister, service.LastRegistrationStatus);
        Assert.Null(regStore.Value);
    }

    [Fact]
    public void Recording_UsesHookOnlyWhileRecording_AndCapturesWinVWithoutSyntheticInjection()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: true, out _);
        var keyboard = new FakeKeyboard(service);
        HotkeyConfig? recorded = null;
        service.HotkeyRecorded += (s, cfg) => recorded = cfg;

        service.StartRecording();

        // 1. Hold Win
        Assert.Equal(IntPtr.Zero, keyboard.Down(VK_LWIN));
        Assert.Null(recorded);

        // 2. Press V -> captures "Win + V", swallows V down, and automatically stops recording
        Assert.Equal((IntPtr)1, keyboard.Down(VK_V));
        Assert.NotNull(recorded);
        Assert.Equal(KeyModifiers.WindowsOrCommand, recorded!.Modifiers);
        Assert.Equal((int)VK_V, recorded.KeyCode);
        Assert.Equal("Win + V", recorded.DisplayString);

        // 3. Release keys -> passed through cleanly
        Assert.Equal(IntPtr.Zero, keyboard.Up(VK_V));
        Assert.Equal(IntPtr.Zero, keyboard.Up(VK_LWIN));
    }

    [Fact]
    public void Recording_WhenWinF1Pressed_CapturesWinModifierAndFormatsWinF1()
    {
        using var service = CreateIsolatedService(nativeRegisterResult: true, out _);
        var keyboard = new FakeKeyboard(service);
        HotkeyConfig? recorded = null;
        service.HotkeyRecorded += (s, cfg) => recorded = cfg;

        service.StartRecording();

        // Hold Win then press F1 (0x70) -> must record "Win + F1", NOT bare "F1"
        Assert.Equal(IntPtr.Zero, keyboard.Down(VK_LWIN));
        Assert.Equal((IntPtr)1, keyboard.Down(0x70));

        Assert.NotNull(recorded);
        Assert.Equal(KeyModifiers.WindowsOrCommand, recorded!.Modifiers);
        Assert.Equal(0x70, recorded.KeyCode);
        Assert.Equal("Win + F1", recorded.DisplayString);
    }

    private sealed class FakeRegistryStore
    {
        public string? Value { get; set; }
    }

    private static WindowsHotkeyService CreateIsolatedService(bool nativeRegisterResult, out FakeRegistryStore store)
    {
        var regStore = new FakeRegistryStore();
        store = regStore;
        return new WindowsHotkeyService
        {
            NativeRegisterHotKeyOverride = (_, _, _, _) => nativeRegisterResult,
            NativeUnregisterHotKeyOverride = (_, _) => true,
            ReadDisabledHotkeysOverride = () => regStore.Value,
            WriteDisabledHotkeysOverride = val => regStore.Value = val
        };
    }

    private sealed class FakeKeyboard
    {
        private readonly WindowsHotkeyService _service;

        public FakeKeyboard(WindowsHotkeyService service)
        {
            _service = service;
        }

        public IntPtr Down(uint vk) => _service.ProcessKeyEvent(WM_KEYDOWN, vk, 0, isInjected: false);

        public IntPtr Up(uint vk) => _service.ProcessKeyEvent(WM_KEYUP, vk, 0, isInjected: false);
    }
}
