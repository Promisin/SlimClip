using System;
using System.IO;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class HotkeyRecordingTests
{
    private class MockHotkeyService : IHotkeyService
    {
#pragma warning disable CS0067
        public event EventHandler? HotkeyPressed;
        public event EventHandler<HotkeyConfig>? HotkeyRecorded;
#pragma warning restore CS0067

        public bool IsRecording { get; private set; }
        public HotkeyConfig? RegisteredConfig { get; private set; }

        public bool Register(HotkeyConfig config)
        {
            RegisteredConfig = config;
            return true;
        }

        public void Unregister()
        {
            RegisteredConfig = null;
        }

        public void StartRecording()
        {
            IsRecording = true;
        }

        public void StopRecording()
        {
            IsRecording = false;
        }

        public void SimulateKeyRecorded(HotkeyConfig config)
        {
            HotkeyRecorded?.Invoke(this, config);
        }

        public void Dispose() { }
    }

    [Fact]
    public void SettingsViewModel_StartRecording_NotifiesHotkeyService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        Assert.False(vm.IsRecordingHotkey);
        Assert.False(mockHotkeyService.IsRecording);

        vm.StartRecordingHotkey();

        Assert.True(vm.IsRecordingHotkey);
        Assert.True(mockHotkeyService.IsRecording, "IHotkeyService.StartRecording must be called to engage low-level keyboard hook");
    }

    [Fact]
    public void SettingsViewModel_RecordingWinV_UpdatesHotkeyAndStopsRecording()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        vm.StartRecordingHotkey();
        Assert.True(vm.IsRecordingHotkey);

        // Simulate recording Win+V from low-level hook
        var winV = new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = 86, // VK_V
            DisplayString = "Win + V",
            IsEnabled = true
        };

        mockHotkeyService.SimulateKeyRecorded(winV);

        Assert.False(vm.IsRecordingHotkey, "Recording should stop once hotkey is captured");
        Assert.Equal("Win + V", vm.HotkeyDisplay);
        Assert.Equal(86, settingsService.Settings.Hotkey.KeyCode);
        Assert.Equal(KeyModifiers.WindowsOrCommand, settingsService.Settings.Hotkey.Modifiers);
    }

    [Fact]
    public void SettingsViewModel_RecordingBareLetterKey_IsIgnoredAndDoesNotSave()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var originalDisplay = settingsService.Settings.Hotkey.DisplayString;
        var originalKeyCode = settingsService.Settings.Hotkey.KeyCode;
        var originalMods = settingsService.Settings.Hotkey.Modifiers;

        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        vm.StartRecordingHotkey();
        Assert.True(vm.IsRecordingHotkey);

        // Attempt to apply a bare letter 'V' without any modifiers
        var bareV = new HotkeyConfig
        {
            Modifiers = KeyModifiers.None,
            KeyCode = 86, // VK_V
            DisplayString = "V",
            IsEnabled = true
        };

        mockHotkeyService.SimulateKeyRecorded(bareV);

        // Must reject: state should remain recording, and original hotkey remains intact
        Assert.True(vm.IsRecordingHotkey, "Recording should NOT stop when a bare non-function key is received");
        Assert.Equal(originalDisplay, vm.HotkeyDisplay);
        Assert.Equal(originalKeyCode, settingsService.Settings.Hotkey.KeyCode);
        Assert.Equal(originalMods, settingsService.Settings.Hotkey.Modifiers);
    }

    [Fact]
    public void SettingsViewModel_RecordingFunctionKeyAlone_IsAccepted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        vm.StartRecordingHotkey();
        Assert.True(vm.IsRecordingHotkey);

        // Function keys (F1-F24) are allowed without modifiers
        var f12 = new HotkeyConfig
        {
            Modifiers = KeyModifiers.None,
            KeyCode = 0x7B, // VK_F12
            DisplayString = "F12",
            IsEnabled = true
        };

        mockHotkeyService.SimulateKeyRecorded(f12);

        Assert.False(vm.IsRecordingHotkey, "Recording should stop once a valid function key is captured");
        Assert.Equal("F12", vm.HotkeyDisplay);
        Assert.Equal(0x7B, settingsService.Settings.Hotkey.KeyCode);
        Assert.Equal(KeyModifiers.None, settingsService.Settings.Hotkey.Modifiers);
    }

    [Fact]
    public void SettingsViewModel_ResetHotkeyDefault_SetsCorrectKeyCode_ForCurrentPlatform()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        // 修改当前快捷键为某个非默认值
        vm.ApplyRecordedHotkey(KeyModifiers.Control, 65, "Ctrl + A");
        Assert.Equal(65, settingsService.Settings.Hotkey.KeyCode);

        // 执行重置默认
        vm.ResetHotkeyDefaultCommand.Execute(null);

        var isMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
        var expectedKeyCode = isMac ? 9 : 86;
        var expectedDisplay = isMac ? "⇧⌘V" : "Alt + Shift + V";

        Assert.Equal(expectedKeyCode, settingsService.Settings.Hotkey.KeyCode);
        Assert.Equal(expectedDisplay, settingsService.Settings.Hotkey.DisplayString);
        Assert.True(settingsService.Settings.Hotkey.IsEnabled);
    }

    [Fact]
    public void SettingsViewModel_ClearHotkey_DisablesHotkeyAndStopsRecording()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        vm.StartRecordingHotkey();
        Assert.True(vm.IsRecordingHotkey);

        vm.ClearHotkey();

        Assert.False(vm.IsRecordingHotkey);
        Assert.False(settingsService.Settings.Hotkey.IsEnabled);
        Assert.Equal("无", vm.HotkeyDisplay);
    }
}
