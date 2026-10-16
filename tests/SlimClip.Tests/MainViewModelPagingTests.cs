using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class MainViewModelPagingTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public MainViewModelPagingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_VMPagingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "test_vmpaging.db");
        _imagesDir = Path.Combine(_testDir, "images");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task LoadNextPageAsync_AppendsItemsSequentially_UntilHasMoreItemsFalse()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        // 插入 120 条数据
        for (int i = 1; i <= 120; i++)
        {
            await history.RecordAsync(new ClipboardData
            {
                Type = ClipboardType.Text,
                Text = $"Message #{i:D3}"
            });
        }

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        // 初始加载第一页 50 条
        Assert.Equal(50, vm.Items.Count);
        Assert.True(vm.HasMoreItems);
        Assert.Equal("Message #120", vm.Items[0].Text);

        // 加载第二页 50 条 -> 总共 100 条
        await vm.LoadNextPageAsync();
        Assert.Equal(100, vm.Items.Count);
        Assert.True(vm.HasMoreItems);

        // 加载第三页剩余 20 条 -> 总共 120 条
        await vm.LoadNextPageAsync();
        Assert.Equal(120, vm.Items.Count);
        Assert.False(vm.HasMoreItems);

        // 再次调用不会重复拉取
        await vm.LoadNextPageAsync();
        Assert.Equal(120, vm.Items.Count);
    }

    [Fact]
    public async Task SearchAndFilter_DebouncesAndResetsPagination()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        for (int i = 1; i <= 60; i++)
        {
            await history.RecordAsync(new ClipboardData
            {
                Type = ClipboardType.Text,
                Text = i % 2 == 0 ? $"Even item #{i}" : $"Odd item #{i}"
            });
        }

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        bool scrollToHomeTriggered = false;
        vm.RequestScrollToHome += () => scrollToHomeTriggered = true;

        vm.SearchDebounceMs = 200;
        // 设置搜索词 "Even"
        vm.SearchText = "Even";
        // 等待防抖完成
        await Task.Delay(350);

        Assert.True(scrollToHomeTriggered);
        Assert.True(vm.Items.Count <= 30);
        Assert.All(vm.Items, item => Assert.Contains("Even", item.Text));
    }

    [Fact]
    public async Task CopyAndEdit_OnDemandLoadsFullContent_WhenSlimProjected()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var recorded = await history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Plain Preview Text",
            HtmlContent = "<b>Bold HTML Content</b>",
            RtfContent = "{\\rtf1 Rich Text}"
        });

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        Assert.Single(vm.Items);
        var loadedItem = vm.Items[0];
        // 初始轻量投影排除了 HtmlContent
        Assert.Null(loadedItem.HtmlContent);

        // 触发编辑前，调用 EnsureItemContentAsync 或在 StartEditItem 中自动补全
        vm.StartEditItem(loadedItem);
        // 等待编辑准备好
        Assert.True(vm.IsEditingItem);
        Assert.True(vm.HasRichText);
        Assert.Equal("<b>Bold HTML Content</b>", vm.EditingText);

        // 测试复制动作触发按需补全
        await vm.CopyItemToClipboardAsync(loadedItem);
        Assert.NotNull(pasteService.LastPastedItem);
        Assert.Equal("<b>Bold HTML Content</b>", pasteService.LastPastedItem.HtmlContent);
    }
}
