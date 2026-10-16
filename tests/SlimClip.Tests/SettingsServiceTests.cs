using System;
using System.IO;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void SettingsService_DefaultValues_MatchSpecification()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Settings_Test_" + Guid.NewGuid().ToString("N"));
        var service = new SettingsService(customDataDir: tempDir);
        try
        {
            Assert.NotNull(service.Settings);
            Assert.Equal(100, service.Settings.MaxHistoryItems);
            Assert.True(service.Settings.RunInBackground);
            Assert.False(service.Settings.AutoCleanEnabled);
            Assert.False(service.Settings.PeriodicCleanEnabled);
            Assert.Equal(7, service.Settings.CleanPeriodDays);
            Assert.NotNull(service.Settings.Hotkey);
            var expectedHotkey = OperatingSystem.IsMacOS() ? "⇧⌘V" : "Alt + Shift + V";
            Assert.Equal(expectedHotkey, service.Settings.Hotkey.DisplayString);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SettingsService_ResolveDataDirectory_RespectsPlatform()
    {
        var dir = SettingsService.ResolveDataDirectory();
        Assert.False(string.IsNullOrWhiteSpace(dir));
        // On Windows test environment, should be BaseDirectory
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(AppDomain.CurrentDomain.BaseDirectory, dir);
        }
    }

    private class FakePlatformHook : IPlatformSettingsHook
    {
        public bool AutoStartStatus { get; set; }
        public bool IsElevatedStatus { get; set; }
        public bool SetAutoStart(bool enabled) { AutoStartStatus = enabled; return true; }
        public System.Threading.Tasks.Task<bool> RequestElevationRestartAsync() => System.Threading.Tasks.Task.FromResult(false);
        public bool IsAutoStartEnabled() => AutoStartStatus;
        public bool IsElevated => IsElevatedStatus;
    }

    [Fact]
    public void SettingsService_SyncsAutoStart_FromPlatformHook()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Settings_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var hook = new FakePlatformHook { AutoStartStatus = true };
            var service = new SettingsService(hook, customDataDir: tempDir);
            Assert.True(service.Settings.StartWithSystem);
            Assert.True(service.IsAutoStartEnabled());

            service.SetAutoStart(false);
            Assert.False(service.Settings.StartWithSystem);
            Assert.False(hook.AutoStartStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SettingsService_ReportsIsElevated_FromPlatformHook()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), "SlimClip_Settings_Test_" + Guid.NewGuid().ToString("N"));
        var tempDir2 = Path.Combine(Path.GetTempPath(), "SlimClip_Settings_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var hook = new FakePlatformHook { IsElevatedStatus = true };
            var service = new SettingsService(hook, customDataDir: tempDir1);
            Assert.True(service.IsElevated);

            var serviceNoHook = new SettingsService(customDataDir: tempDir2);
            Assert.False(serviceNoHook.IsElevated);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
        }
    }
}
