using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Helpers;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class AboutPanelTests
{
    private class DummyHistory : IClipboardHistory
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<ClipboardItem> RecordAsync(ClipboardData data) => throw new NotImplementedException();
        public Task<List<ClipboardItem>> GetAllItemsAsync() => Task.FromResult(new List<ClipboardItem>());
        public Task<bool> DeleteItemAsync(string id) => Task.FromResult(true);
        public Task<bool> ToggleFavoriteAsync(string id) => Task.FromResult(true);
        public Task ClearAllAsync() => Task.CompletedTask;
        public Task TrimExcessAsync(int maxItems) => Task.CompletedTask;
        public Task CleanPeriodicAsync(int days) => Task.CompletedTask;
        public Task UpdateTimestampAsync(string id, DateTime timestamp) => Task.CompletedTask;
        public Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt) => Task.FromResult(true);
        public void Dispose() { }
    }

    [Fact]
    public void SettingsViewModel_AboutProperties_ReturnValidStrings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_About_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(customDataDir: tempDir);
        settingsService.Settings.Language = "zh-CN";
        var loc = new LocalizationService("zh-CN");
        var vm = new SettingsViewModel(settingsService, new DummyHistory(), localizationService: loc);

        // 1. AppVersion should start with 'v' and not be empty
        Assert.False(string.IsNullOrWhiteSpace(vm.AppVersion));
        Assert.StartsWith("v", vm.AppVersion);

        // 2. PlatformDisplay should contain OS information
        Assert.False(string.IsNullOrWhiteSpace(vm.PlatformDisplay));
        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("macOS", vm.PlatformDisplay);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.Contains("Windows", vm.PlatformDisplay);
        }

        // 3. VersionInfoSummary should combine AppVersion and PlatformDisplay
        Assert.Contains(vm.AppVersion, vm.VersionInfoSummary);
        Assert.Contains(vm.PlatformDisplay, vm.VersionInfoSummary);

        // 4. Default button text
        Assert.Equal(loc["Settings_About_CopyInfo"], vm.CopyVersionButtonText);
    }

    [Fact]
    public async Task SettingsViewModel_CopyVersionInfo_FiresEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_About_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(customDataDir: tempDir);
        settingsService.Settings.Language = "zh-CN";
        var loc = new LocalizationService("zh-CN");
        var vm = new SettingsViewModel(settingsService, new DummyHistory(), localizationService: loc);


        string? copiedText = null;
        vm.CopyToClipboardRequested += text => copiedText = text;

        var copyTask = vm.CopyVersionInfoAsync();

        // 立即触发了事件且按钮文字变化
        Assert.NotNull(copiedText);
        Assert.Equal(vm.VersionInfoSummary, copiedText);
        Assert.Equal(loc["Settings_About_Copied"], vm.CopyVersionButtonText);

        await copyTask;
        Assert.Equal(loc["Settings_About_CopyInfo"], vm.CopyVersionButtonText);
    }


    [Fact]
    public void SettingsViewModel_Links_MatchCanonicalUrls()
    {
        Assert.Equal("https://github.com/Promisin/SlimClip", SettingsViewModel.ProjectUrl);
        Assert.Equal("https://github.com/Promisin/SlimClip/releases", SettingsViewModel.ReleasesUrl);
        Assert.Equal("https://github.com/Promisin/SlimClip/issues", SettingsViewModel.IssuesUrl);
        Assert.Equal("https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans", SettingsViewModel.LicenseUrl);
    }

    [Fact]
    public void PlatformLauncher_HandlesInvalidInputGracefully()
    {
        // Null or whitespace inputs should safely return false without throwing
        Assert.False(PlatformLauncher.OpenUrl(""));
        Assert.False(PlatformLauncher.OpenUrl("   "));
        Assert.False(PlatformLauncher.OpenFolder(""));
        Assert.False(PlatformLauncher.OpenFolder("   "));
    }

    [Fact]
    public void PlatformLauncher_OpenFolder_WithSpacesInPath_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip Space Test Dir " + Guid.NewGuid().ToString("N"));
        try
        {
            using var proc = PlatformLauncher.StartFolderProcess(tempDir);
            Assert.NotNull(proc);
            if (OperatingSystem.IsMacOS())
            {
                proc.WaitForExit(5000);
                Assert.Equal(0, proc.ExitCode);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
