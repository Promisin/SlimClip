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
        public HotkeyRegistrationStatus NextStatus { get; set; } = HotkeyRegistrationStatus.Success;
        public HotkeyRegistrationStatus LastRegistrationStatus { get; private set; } = HotkeyRegistrationStatus.Success;
        public bool AllowSystemOverride { get; private set; }
        public int RestartExplorerCallCount { get; private set; }

        public bool Register(HotkeyConfig config)
        {
            RegisteredConfig = config;
            LastRegistrationStatus = NextStatus;
            return LastRegistrationStatus == HotkeyRegistrationStatus.Success;
        }

        public void Unregister()
        {
            RegisteredConfig = null;
        }

        public void ConfigureSystemHotkeyOverride(bool allowOverride, string? initialManagedChar = null, Action<string>? onManagedCharChanged = null)
        {
            AllowSystemOverride = allowOverride;
        }

        public bool RestartExplorerAndReRegister()
        {
            RestartExplorerCallCount++;
            LastRegistrationStatus = HotkeyRegistrationStatus.Success;
            return true;
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
    public void SettingsViewModel_ToggleRecording_StartsAndCancelsRecording()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService();
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);

        Assert.False(vm.IsRecordingHotkey);
        Assert.Equal(settingsService.Settings.Hotkey.DisplayString, vm.HotkeyBoxText);

        // 1st click on Direct Hotkey Recorder starts recording
        vm.ToggleRecordingHotkeyCommand.Execute(null);
        Assert.True(vm.IsRecordingHotkey);
        Assert.True(mockHotkeyService.IsRecording);
        Assert.Equal(vm.L["Settings_Hotkey_Recording"], vm.HotkeyBoxText);

        // 2nd click cancels recording and restores original display text
        vm.ToggleRecordingHotkeyCommand.Execute(null);
        Assert.False(vm.IsRecordingHotkey);
        Assert.False(mockHotkeyService.IsRecording);
        Assert.Equal(settingsService.Settings.Hotkey.DisplayString, vm.HotkeyBoxText);
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
    public void SettingsViewModel_WhenWinVOccupied_ExposesImmediateFeedbackAndExplorerRestartFlow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(null, tempDir);
        var mockHotkeyService = new MockHotkeyService
        {
            NextStatus = HotkeyRegistrationStatus.OccupiedCanOverrideSystem
        };
        var vm = new SettingsViewModel(settingsService, new ClipboardHistory(":memory:", tempDir), mockHotkeyService);
        vm.HotkeyChanged += (_, cfg) => mockHotkeyService.Register(cfg);

        // 1. User records Win + V while override is off -> immediate warning feedback
        vm.ApplyRecordedHotkey(KeyModifiers.WindowsOrCommand, 86, "Win + V");
        vm.RefreshHotkeyRegistrationStatus();

        Assert.True(vm.HasHotkeyStatusMessage);
        Assert.False(vm.NeedsExplorerRestart);

        // 2. User enables AllowDisableSystemHotkeys -> status transitions to PendingExplorerRestart
        mockHotkeyService.NextStatus = HotkeyRegistrationStatus.PendingExplorerRestart;
        vm.AllowDisableSystemHotkeys = true;

        Assert.True(vm.AllowDisableSystemHotkeys);
        Assert.True(mockHotkeyService.AllowSystemOverride);
        Assert.True(vm.NeedsExplorerRestart);
        Assert.True(vm.HasHotkeyStatusMessage);

        // 3. User clicks Restart Explorer button -> re-registers and clears warning
        vm.RestartExplorerCommand.Execute(null);
        Assert.Equal(1, mockHotkeyService.RestartExplorerCallCount);
        Assert.False(vm.NeedsExplorerRestart);
        Assert.False(vm.HasHotkeyStatusMessage);
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

        var bareV = new HotkeyConfig
        {
            Modifiers = KeyModifiers.None,
            KeyCode = 86, // VK_V
            DisplayString = "V",
            IsEnabled = true
        };

        mockHotkeyService.SimulateKeyRecorded(bareV);

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

        vm.ApplyRecordedHotkey(KeyModifiers.Control, 65, "Ctrl + A");
        Assert.Equal(65, settingsService.Settings.Hotkey.KeyCode);

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
