using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class AutoUpdateSettingsTests
{
    private sealed class StubUpdateService : IUpdateService
    {
        public UpdateCheckResult NextResult { get; set; } = UpdateCheckResult.UpToDate();
        public int CheckCallCount { get; private set; }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
        {
            CheckCallCount++;
            return Task.FromResult(NextResult);
        }

        public Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(1.0);
            return Task.FromResult(@"C:\Temp\SlimClip-v1.0.8-windows-x64-setup.exe");
        }

        public void LaunchInstallerAndExit(string installerPath) { }
    }

    private sealed class StubHistory : IClipboardHistory
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
        public Task<List<ClipboardItem>> GetPagedItemsAsync(int limit, int offset, ClipboardType? type = null, bool favoriteOnly = false, string? searchText = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<ClipboardItem>());
        public Task<(string? HtmlContent, string? RtfContent)> GetItemContentAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<(string?, string?)>((null, null));
        public Task<StorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StorageBreakdown());
        public Task<int> CleanTargetedAsync(TargetedCleanFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() { }
    }

    private static UpdateInfo CreateUpdateInfo(string tagName = "v1.0.8") => new()
    {
        Version = tagName.TrimStart('v'),
        TagName = tagName,
        ReleaseNotes = "### 新增\n- 自动检查更新",
        AssetName = $"SlimClip-{tagName}-windows-x64-setup.exe",
        DownloadUrl = $"https://example.com/{tagName}/setup.exe",
        AssetSize = 1024,
        HtmlUrl = $"https://example.com/{tagName}"
    };

    [Fact]
    public void AppSettings_AutoCheckUpdates_DefaultsToFalse_AndPersistsRoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_AutoUpdate_Cfg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            Assert.False(settingsService.Settings.AutoCheckUpdates);

            settingsService.Settings.AutoCheckUpdates = true;
            settingsService.Save();

            var reloaded = new SettingsService(customDataDir: tempDir);
            Assert.True(reloaded.Settings.AutoCheckUpdates);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SettingsViewModel_AutoCheckUpdates_SavesAndRaisesChangedEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_AutoUpdate_VM_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var vm = new SettingsViewModel(settingsService, new StubHistory());

            var events = new List<bool>();
            vm.AutoCheckUpdatesChanged += (_, enabled) => events.Add(enabled);

            Assert.False(vm.AutoCheckUpdates);

            vm.AutoCheckUpdates = true;
            Assert.True(settingsService.Settings.AutoCheckUpdates);
            Assert.Equal([true], events);

            // Setting identical value does not emit duplicate event
            vm.AutoCheckUpdates = true;
            Assert.Single(events);

            vm.AutoCheckUpdates = false;
            Assert.False(settingsService.Settings.AutoCheckUpdates);
            Assert.Equal([true, false], events);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckUpdatesSilentlyAsync_WhenNewVersionFound_UpdatesStateAndDeduplicatesNotification()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Silent_Found_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var updateService = new StubUpdateService
            {
                NextResult = UpdateCheckResult.Found(CreateUpdateInfo("v1.0.8"))
            };
            var loc = new LocalizationService("zh-CN");
            var vm = new SettingsViewModel(settingsService, new StubHistory(), localizationService: loc, updateService: updateService);

            var notifiedTags = new List<string>();
            vm.AutoUpdateAvailableDetected += (_, info) => notifiedTags.Add(info.TagName);

            await vm.CheckUpdatesSilentlyAsync();

            Assert.True(vm.HasNewVersion);
            Assert.NotNull(vm.AvailableUpdate);
            Assert.Equal("v1.0.8", vm.AvailableUpdate!.TagName);
            Assert.Equal(loc["Settings_About_Update_Found"], vm.CheckUpdateButtonText);
            Assert.Equal(["v1.0.8"], notifiedTags);

            // Second silent check for same tag must not trigger duplicate notification
            await vm.CheckUpdatesSilentlyAsync();
            Assert.Equal(["v1.0.8"], notifiedTags);

            // Newer tag must notify again
            updateService.NextResult = UpdateCheckResult.Found(CreateUpdateInfo("v1.0.9"));
            await vm.CheckUpdatesSilentlyAsync();
            Assert.Equal(["v1.0.8", "v1.0.9"], notifiedTags);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckUpdatesSilentlyAsync_WhenFailedOrUpToDate_DoesNotAlterButtonTextOrNotify()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Silent_Quiet_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var updateService = new StubUpdateService
            {
                NextResult = UpdateCheckResult.Failed("network timeout")
            };
            var loc = new LocalizationService("zh-CN");
            var vm = new SettingsViewModel(settingsService, new StubHistory(), localizationService: loc, updateService: updateService);
            var initialButtonText = vm.CheckUpdateButtonText;

            int notifyCount = 0;
            vm.AutoUpdateAvailableDetected += (_, _) => notifyCount++;

            await vm.CheckUpdatesSilentlyAsync();
            Assert.False(vm.HasNewVersion);
            Assert.Equal(initialButtonText, vm.CheckUpdateButtonText);
            Assert.Equal(0, notifyCount);

            updateService.NextResult = UpdateCheckResult.UpToDate();
            await vm.CheckUpdatesSilentlyAsync();
            Assert.False(vm.HasNewVersion);
            Assert.Equal(initialButtonText, vm.CheckUpdateButtonText);
            Assert.Equal(0, notifyCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckUpdatesSilentlyAsync_WhenUpdateAlreadyDownloaded_SkipsNetworkCheck()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Silent_Skip_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var updateService = new StubUpdateService
            {
                NextResult = UpdateCheckResult.Found(CreateUpdateInfo("v1.0.8"))
            };
            var vm = new SettingsViewModel(settingsService, new StubHistory(), updateService: updateService);

            await vm.CheckUpdatesAsync();
            await vm.StartDownloadUpdateAsync();
            Assert.True(vm.IsUpdateDownloaded);

            int callsBefore = updateService.CheckCallCount;
            await vm.CheckUpdatesSilentlyAsync();
            Assert.Equal(callsBefore, updateService.CheckCallCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Localization_AutoCheckUpdatesKeys_ExistAndFormat(string lang)
    {
        var loc = new LocalizationService(lang);

        Assert.NotEqual("Settings_General_AutoCheckUpdate", loc["Settings_General_AutoCheckUpdate"]);
        Assert.NotEqual("Settings_General_AutoCheckUpdate_Desc", loc["Settings_General_AutoCheckUpdate_Desc"]);
        Assert.NotEqual("Update_Notify_Available_Title", loc["Update_Notify_Available_Title"]);
        Assert.NotEqual("Update_Notify_Available_Body", loc["Update_Notify_Available_Body"]);

        Assert.Contains("v1.0.8", string.Format(loc["Update_Notify_Available_Title"], "v1.0.8"));
    }
}
