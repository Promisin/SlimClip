using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.Desktop;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class UpdateNotificationTests
{
    private sealed class StubUpdateService : IUpdateService
    {
        public bool ShouldFailDownload { get; init; }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
        {
            return Task.FromResult(UpdateCheckResult.Found(new UpdateInfo
            {
                Version = "1.0.8",
                TagName = "v1.0.8",
                ReleaseNotes = "### 新增\n- 下载完成通知",
                AssetName = "SlimClip-v1.0.8-windows-x64-setup.exe",
                DownloadUrl = "https://example.com/setup.exe",
                AssetSize = 1024,
                HtmlUrl = "https://example.com/release"
            }));
        }

        public Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (ShouldFailDownload)
            {
                throw new InvalidOperationException("network error");
            }

            progress?.Report(1.0);
            return Task.FromResult(@"C:\Temp\SlimClip-v1.0.8-windows-x64-setup.exe");
        }

        public void LaunchInstallerAndExit(string installerPath) { }
    }

    private sealed class StubHistory : IClipboardHistory
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<ClipboardItem> RecordAsync(ClipboardData data) => throw new NotImplementedException();
        public Task<System.Collections.Generic.List<ClipboardItem>> GetAllItemsAsync() => Task.FromResult(new System.Collections.Generic.List<ClipboardItem>());
        public Task<bool> DeleteItemAsync(string id) => Task.FromResult(true);
        public Task<bool> ToggleFavoriteAsync(string id) => Task.FromResult(true);
        public Task ClearAllAsync() => Task.CompletedTask;
        public Task TrimExcessAsync(int maxItems) => Task.CompletedTask;
        public Task CleanPeriodicAsync(int days) => Task.CompletedTask;
        public Task UpdateTimestampAsync(string id, DateTime timestamp) => Task.CompletedTask;
        public Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt) => Task.FromResult(true);
        public Task<System.Collections.Generic.List<ClipboardItem>> GetPagedItemsAsync(int limit, int offset, ClipboardType? type = null, bool favoriteOnly = false, string? searchText = null, CancellationToken cancellationToken = default) => Task.FromResult(new System.Collections.Generic.List<ClipboardItem>());
        public Task<(string? HtmlContent, string? RtfContent)> GetItemContentAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<(string?, string?)>((null, null));
        public Task<StorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StorageBreakdown());
        public Task<int> CleanTargetedAsync(TargetedCleanFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() { }
    }

    [Fact]
    public async Task StartDownloadUpdateAsync_OnSuccess_RaisesUpdateDownloadCompleted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Notify_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var vm = new SettingsViewModel(settingsService, new StubHistory(), updateService: new StubUpdateService());

            await vm.CheckUpdatesAsync();

            UpdateInfo? notifiedUpdate = null;
            int eventCount = 0;
            vm.UpdateDownloadCompleted += (_, info) =>
            {
                eventCount++;
                notifiedUpdate = info;
            };

            await vm.StartDownloadUpdateAsync();

            Assert.Equal(1, eventCount);
            Assert.NotNull(notifiedUpdate);
            Assert.Equal("v1.0.8", notifiedUpdate!.TagName);
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
    public async Task StartDownloadUpdateAsync_OnFailure_DoesNotRaiseUpdateDownloadCompleted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Notify_Fail_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(customDataDir: tempDir);
            var vm = new SettingsViewModel(
                settingsService,
                new StubHistory(),
                updateService: new StubUpdateService { ShouldFailDownload = true });

            await vm.CheckUpdatesAsync();

            int eventCount = 0;
            vm.UpdateDownloadCompleted += (_, _) => eventCount++;

            await vm.StartDownloadUpdateAsync();

            Assert.True(vm.IsDownloadFailed);
            Assert.Equal(0, eventCount);
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
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldShowDownloadNotification_MatchesWindowVisibilityAndActivation(
        bool isWindowVisible,
        bool isWindowActive,
        bool expected)
    {
        Assert.Equal(expected, App.ShouldShowDownloadNotification(isWindowVisible, isWindowActive));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Localization_UpdateNotificationKeys_FormatCleanly(string lang)
    {
        var loc = new LocalizationService(lang);

        var title = loc["Update_Notify_Downloaded_Title"];
        var bodyTemplate = loc["Update_Notify_Downloaded_Body"];

        Assert.NotEqual("Update_Notify_Downloaded_Title", title);
        Assert.NotEqual("Update_Notify_Downloaded_Body", bodyTemplate);

        var formattedBody = string.Format(bodyTemplate, "v1.0.8");
        Assert.Contains("v1.0.8", formattedBody);
    }
}
