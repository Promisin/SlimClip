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

public class UpdateProgressTests
{
    private class MockUpdateService : IUpdateService
    {
        public Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
        {
            return Task.FromResult(UpdateCheckResult.Found(new UpdateInfo
            {
                Version = "1.0.7",
                TagName = "v1.0.7",
                ReleaseNotes = "### 修复\n- 修复问题",
                AssetName = "SlimClip-setup.exe",
                DownloadUrl = "https://example.com/test",
                AssetSize = 1000,
                HtmlUrl = "https://example.com"
            }));
        }

        public async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(0.1);
            await Task.Yield();
            progress?.Report(0.5);
            await Task.Yield();
            progress?.Report(1.0);
            return @"C:\temp\SlimClip-setup.exe";
        }

        public void LaunchInstallerAndExit(string installerPath) { }
    }

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
        public Task<List<ClipboardItem>> GetPagedItemsAsync(int limit, int offset, ClipboardType? type = null, bool favoriteOnly = false, string? searchText = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<ClipboardItem>());
        public Task<(string? HtmlContent, string? RtfContent)> GetItemContentAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<(string?, string?)>((null, null));
        public Task<StorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StorageBreakdown());
        public Task<int> CleanTargetedAsync(TargetedCleanFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() { }
    }

    [Fact]
    public async Task StartDownloadUpdateAsync_ReportsPercentageScaledTo100()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Progress_Test_" + Guid.NewGuid().ToString("N"));
        var settingsService = new SettingsService(customDataDir: tempDir);
        var mockUpdateService = new MockUpdateService();
        var vm = new SettingsViewModel(settingsService, new DummyHistory(), updateService: mockUpdateService);

        // 模拟检测到更新
        await vm.CheckUpdatesAsync();
        Assert.True(vm.HasNewVersion);
        Assert.NotNull(vm.AvailableUpdate);

        var reportedValues = new List<double>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.DownloadProgress))
            {
                reportedValues.Add(vm.DownloadProgress);
            }
        };

        await vm.StartDownloadUpdateAsync();

        Assert.True(vm.IsUpdateDownloaded);
        Assert.False(vm.IsDownloadingUpdate);
        Assert.Equal(100.0, vm.DownloadProgress);

        // 验证进度值被正确放缩至 0..100 范围，而非 0..1
        // 如果未放缩，值只会是 0.1, 0.5, 1.0；放缩后应包含 10.0, 50.0, 100.0
        Assert.Contains(reportedValues, p => p > 1.0);
    }
}
