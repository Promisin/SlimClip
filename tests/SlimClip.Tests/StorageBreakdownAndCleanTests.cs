using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class StorageBreakdownAndCleanTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;
    private readonly ClipboardHistory _history;

    public StorageBreakdownAndCleanTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Storage_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _imagesDir = Path.Combine(_tempDir, "images");
        _history = new ClipboardHistory(_dbPath, _imagesDir);
        _history.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _history.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetStorageBreakdownAsync_ReturnsZero_WhenEmpty()
    {
        var breakdown = await _history.GetStorageBreakdownAsync();
        Assert.NotNull(breakdown);
        Assert.Equal(0, breakdown.TotalCount);
        Assert.Equal(0, breakdown.TotalBytes);
        Assert.Equal(0, breakdown.TextCount);
        Assert.Equal(0, breakdown.ImageCount);
        Assert.Equal(0, breakdown.FileCount);
        Assert.Equal(0, breakdown.FavoriteCount);
    }

    [Fact]
    public async Task GetStorageBreakdownAsync_CalculatesCountsAndBytesAccurately()
    {
        // 1. 录入纯文本
        await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Hello World! 这是一个测试文本。"
        });

        // 2. 录入富文本 (带 HTML)
        await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Formatted Text",
            HtmlContent = "<b>Formatted Text</b>"
        });

        // 3. 录入图片 (100 字节数据)
        var fakePng = new byte[1024]; // 1 KB
        Random.Shared.NextBytes(fakePng);
        var imgItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Image,
            ImageBytes = fakePng,
            ImageWidth = 100,
            ImageHeight = 100
        });

        // 4. 录入文件
        await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.File,
            FilePaths = new[] { @"C:\test\file1.txt", @"C:\test\file2.png" }
        });

        // 将图片项加入收藏
        await _history.ToggleFavoriteAsync(imgItem.Id);

        var breakdown = await _history.GetStorageBreakdownAsync();

        Assert.Equal(4, breakdown.TotalCount);
        Assert.Equal(2, breakdown.TextCount);
        Assert.Equal(1, breakdown.ImageCount);
        Assert.Equal(1, breakdown.FileCount);
        Assert.Equal(1, breakdown.FavoriteCount);

        // 验证文本有字节
        Assert.True(breakdown.TextBytes > 0);
        // 验证图片物理文件大小统计 (至少包含 1024 字节)
        Assert.True(breakdown.ImageBytes >= 1024);
        // 验证文件引用也有字节
        Assert.True(breakdown.FileBytes > 0);
        // 总字节大于 0
        Assert.True(breakdown.TotalBytes > 1024);
    }

    [Fact]
    public async Task CleanTargetedAsync_FiltersByType_CleansOnlySelectedTypes()
    {
        // 录入 1 个文本、1 个图片、1 个文件
        var textItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Keep this text"
        });

        var fakePng = new byte[512];
        Random.Shared.NextBytes(fakePng);
        var imgItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Image,
            ImageBytes = fakePng,
            ImageWidth = 50,
            ImageHeight = 50
        });

        var fileItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.File,
            FilePaths = new[] { @"C:\data.bin" }
        });

        // 仅清理图片
        var filter = new TargetedCleanFilter
        {
            CleanText = false,
            CleanImages = true,
            CleanFiles = false,
            TimeRange = CleanTimeRange.All,
            PreserveFavorites = true
        };

        var cleanedCount = await _history.CleanTargetedAsync(filter);
        Assert.Equal(1, cleanedCount);

        var all = await _history.GetAllItemsAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Id == textItem.Id);
        Assert.Contains(all, x => x.Id == fileItem.Id);
        Assert.DoesNotContain(all, x => x.Id == imgItem.Id);

        // 确认孤立图片文件已被删除
        var breakdown = await _history.GetStorageBreakdownAsync();
        Assert.Equal(0, breakdown.ImageCount);
        Assert.Equal(0, breakdown.ImageBytes);
    }

    [Fact]
    public async Task CleanTargetedAsync_PreservesFavorites_WhenRequested()
    {
        var textNormal = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Normal Text"
        });

        var textFavorite = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Important Favorite Text"
        });
        await _history.ToggleFavoriteAsync(textFavorite.Id);

        // 清理全部文本，但保留收藏
        var filter = new TargetedCleanFilter
        {
            CleanText = true,
            CleanImages = true,
            CleanFiles = true,
            TimeRange = CleanTimeRange.All,
            PreserveFavorites = true
        };

        var cleanedCount = await _history.CleanTargetedAsync(filter);
        Assert.Equal(1, cleanedCount);

        var all = await _history.GetAllItemsAsync();
        Assert.Single(all);
        Assert.Equal(textFavorite.Id, all[0].Id);
    }

    [Fact]
    public async Task CleanTargetedAsync_FiltersByTimeRange_DeletesOnlyExpiredItems()
    {
        var oldItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Old Item from 10 days ago"
        });
        // 模拟 10 天前复制
        await _history.UpdateTimestampAsync(oldItem.Id, DateTime.Now.AddDays(-10));

        var recentItem = await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Recent Item from 1 day ago"
        });
        await _history.UpdateTimestampAsync(recentItem.Id, DateTime.Now.AddDays(-1));

        // 清理 7 天前 (一周前) 的数据
        var filter = new TargetedCleanFilter
        {
            CleanText = true,
            CleanImages = true,
            CleanFiles = true,
            TimeRange = CleanTimeRange.OlderThanWeek,
            PreserveFavorites = true
        };

        var cleanedCount = await _history.CleanTargetedAsync(filter);
        Assert.Equal(1, cleanedCount);

        var all = await _history.GetAllItemsAsync();
        Assert.Single(all);
        Assert.Equal(recentItem.Id, all[0].Id);
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(52428800, "50.0 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void StorageBreakdown_FormatBytes_FormatsNicely(long bytes, string expected)
    {
        Assert.Equal(expected, StorageBreakdown.FormatBytes(bytes));
    }

    [Fact]
    public async Task SettingsViewModel_StorageBreakdown_DisplaysFormattedStrings()
    {
        await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Sample Text For ViewModel Test"
        });

        var settingsService = new SettingsService(customDataDir: _tempDir);
        settingsService.Settings.Language = "zh-CN";
        var loc = new LocalizationService("zh-CN");
        var vm = new SlimClip.UI.ViewModels.SettingsViewModel(settingsService, _history, localizationService: loc);

        await vm.RefreshStorageBreakdownAsync();

        Assert.NotNull(vm.StorageBreakdown);
        Assert.Equal(1, vm.StorageBreakdown.TotalCount);
        Assert.Contains("已占用", vm.StorageSummaryText);
        Assert.Contains("1", vm.StorageSummaryText);
        Assert.Contains("文本", vm.StorageBreakdownText);
        Assert.Contains("1", vm.CleanTextLabel);
    }

    [Fact]
    public async Task SettingsViewModel_CanConfirmTargetedClean_RequiresAtLeastOneType()
    {
        var settingsService = new SettingsService(customDataDir: _tempDir);
        var loc = new LocalizationService("zh-CN");
        var vm = new SlimClip.UI.ViewModels.SettingsViewModel(settingsService, _history, localizationService: loc);

        Assert.True(vm.CanConfirmTargetedClean);

        vm.CleanTextSelected = false;
        vm.CleanImagesSelected = false;
        vm.CleanFilesSelected = false;

        Assert.False(vm.CanConfirmTargetedClean);

        var cleanedCount = await vm.ExecuteTargetedCleanAsync();
        Assert.Equal(0, cleanedCount);
    }

    [Fact]
    public async Task SettingsViewModel_ExecuteTargetedClean_InvokesDataClearedAndRefreshesBreakdown()
    {
        await _history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Item to clean"
        });

        var settingsService = new SettingsService(customDataDir: _tempDir);
        var loc = new LocalizationService("zh-CN");
        var vm = new SlimClip.UI.ViewModels.SettingsViewModel(settingsService, _history, localizationService: loc);

        await vm.RefreshStorageBreakdownAsync();
        Assert.Equal(1, vm.StorageBreakdown!.TotalCount);

        bool eventFired = false;
        vm.DataCleared += (s, e) => eventFired = true;

        vm.CleanTextSelected = true;
        vm.PreserveFavoritesSelected = false;
        var count = await vm.ExecuteTargetedCleanAsync();

        Assert.Equal(1, count);
        Assert.True(eventFired);
        Assert.Equal(0, vm.StorageBreakdown.TotalCount);
        Assert.Contains("1", vm.CleanSuccessMessage);
    }
}
