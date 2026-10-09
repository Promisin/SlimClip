using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class FakeClipboardMonitor : IClipboardMonitor
{
    public event EventHandler<ClipboardData>? DataCopied;
    public bool IsSuppressed { get; private set; }

    public void Start() { }
    public void Stop() { }

    public void SuppressNextCapture()
    {
        IsSuppressed = true;
    }

    public void TriggerCopy(ClipboardData data)
    {
        if (!IsSuppressed)
        {
            DataCopied?.Invoke(this, data);
        }
        IsSuppressed = false;
    }

    public void Dispose() { }
}

public class FakePasteService : IPasteService
{
    public ClipboardItem? LastPastedItem { get; private set; }
    public int LastDelayMs { get; private set; }

    public Task PasteAsync(ClipboardItem item, int delayMs)
    {
        LastPastedItem = item;
        LastDelayMs = delayMs;
        return Task.CompletedTask;
    }

    public Task SetClipboardDataAsync(ClipboardItem item)
    {
        LastPastedItem = item;
        return Task.CompletedTask;
    }
}

public class MainViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public MainViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_VM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "test.db");
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
    public async Task MainViewModel_FilterAndSearch_WorksCorrectly()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        // Simulate copying items
        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Apple Banana" });
        await Task.Delay(20);
        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Orange Lemon" });
        await Task.Delay(20);
        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Image, ImageBytes = new byte[] { 1, 2, 3 }, ImageWidth = 50, ImageHeight = 50 });

        await Task.Delay(50);

        Assert.Equal(3, vm.Items.Count);

        // Filter by Text
        vm.CurrentFilterType = "Text";
        Assert.Equal(2, vm.Items.Count);

        // Search within Text
        vm.SearchText = "Apple";
        Assert.Single(vm.Items);
        Assert.Equal("Apple Banana", vm.Items[0].Text);

        // Reset filter
        vm.SearchText = string.Empty;
        vm.CurrentFilterType = "All";
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task MainViewModel_Paste_TriggersPasteServiceAndSuppression()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Hello World" });
        await Task.Delay(50);

        Assert.Single(vm.Items);
        var item = vm.Items[0];
        vm.SelectedItem = item;

        await vm.PasteSelectedCommand.ExecuteAsync(null);

        Assert.NotNull(pasteService.LastPastedItem);
        Assert.Equal("Hello World", pasteService.LastPastedItem.Text);
    }

    [Fact]
    public async Task MainViewModel_CycleFilter_ClampsWithoutLooping()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        // Initial state is "All" (idx 1)
        Assert.Equal("All", vm.CurrentFilterType);

        // Stepping left moves to "Favorite" (idx 0)
        vm.CycleFilter(false);
        Assert.Equal("Favorite", vm.CurrentFilterType);

        // Stepping left at boundary should clamp at "Favorite" and NOT wrap around to "File"
        vm.CycleFilter(false);
        Assert.Equal("Favorite", vm.CurrentFilterType);

        // Step right moves back to "All" (idx 1)
        vm.CycleFilter(true);
        Assert.Equal("All", vm.CurrentFilterType);

        // Step right across categories: Text -> Image -> File
        vm.CycleFilter(true);
        Assert.Equal("Text", vm.CurrentFilterType);

        vm.CycleFilter(true);
        Assert.Equal("Image", vm.CurrentFilterType);

        vm.CycleFilter(true);
        Assert.Equal("File", vm.CurrentFilterType);

        // Stepping right at the boundary should clamp at "File" and NOT wrap around to "Favorite"
        vm.CycleFilter(true);
        Assert.Equal("File", vm.CurrentFilterType);

        // Stepping left moves back to Image
        vm.CycleFilter(false);
        Assert.Equal("Image", vm.CurrentFilterType);
    }

    [Fact]
    public async Task MainViewModel_FavoriteFilter_FiltersOnlyFavoritedItems_AndAllTabSortsChronologically()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Oldest 1" });
        await Task.Delay(20);
        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Middle 2" });
        await Task.Delay(20);
        monitor.TriggerCopy(new ClipboardData { Type = ClipboardType.Text, Text = "Newest 3" });
        await Task.Delay(30);

        Assert.Equal(3, vm.Items.Count);

        // Toggle favorite on Oldest 1 (index 2 in All tab)
        var oldestItem = vm.Items[2];
        Assert.Equal("Oldest 1", oldestItem.Text);
        await vm.ToggleFavoriteCommand.ExecuteAsync(oldestItem);
        Assert.True(oldestItem.IsFavorite);

        // In "All" tab, items MUST remain strictly in chronological order: Newest 3 -> Middle 2 -> Oldest 1
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("Newest 3", vm.Items[0].Text);
        Assert.Equal("Middle 2", vm.Items[1].Text);
        Assert.Equal("Oldest 1", vm.Items[2].Text);
        Assert.True(vm.Items[2].IsFavorite);

        // Switch to "Favorite" tab
        vm.CurrentFilterType = "Favorite";
        Assert.Single(vm.Items);
        Assert.Equal("Oldest 1", vm.Items[0].Text);

        // Unfavorite
        await vm.ToggleFavoriteCommand.ExecuteAsync(vm.Items[0]);
        Assert.Empty(vm.Items);

        // Back to "All" tab
        vm.CurrentFilterType = "All";
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task MainViewModel_ResetState_ResetsFiltersAndSelectsFirstItem()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        monitor.TriggerCopy(new ClipboardData { Text = "Item 1" });
        monitor.TriggerCopy(new ClipboardData { Text = "Item 2" });
        monitor.TriggerCopy(new ClipboardData { Text = "Item 3" });

        Assert.Equal(3, vm.Items.Count);

        // Simulate user selecting the 3rd item and applying search/filter
        vm.SelectedItem = vm.Items[2];
        Assert.Equal("Item 1", vm.SelectedItem?.Text);
        vm.SearchText = "Item 2";
        vm.CurrentFilterType = "Text";

        // Call ResetState (triggered when panel is reopened)
        vm.ResetState();

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal("All", vm.CurrentFilterType);
        Assert.Equal(3, vm.Items.Count);
        Assert.NotNull(vm.SelectedItem);
        Assert.Equal("Item 3", vm.SelectedItem.Text);
    }

    [Fact]
    public async Task CopyItemToClipboardAsync_SuppressesCapture_SetsClipboardAndUpdatesTimestamp()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        monitor.TriggerCopy(new ClipboardData { Text = "First Item" });
        await Task.Delay(20);
        monitor.TriggerCopy(new ClipboardData { Text = "Second Item" });
        await Task.Delay(20);

        var firstItem = vm.Items.Last(); // "First Item"
        var originalTime = firstItem.CopiedAt;

        await Task.Delay(20);
        await vm.CopyItemToClipboardAsync(firstItem);

        Assert.True(monitor.IsSuppressed);
        Assert.NotNull(pasteService.LastPastedItem);
        Assert.Equal("First Item", pasteService.LastPastedItem.Text);
        Assert.True(firstItem.CopiedAt >= originalTime);
        Assert.Equal(firstItem, vm.Items.First()); // Moved to top
    }

    [Fact]
    public async Task StartEditItem_TextItem_InitializesStateCorrectly()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        var item = await history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Hello World",
            HtmlContent = "<b>Hello World</b>"
        });

        vm.StartEditItem(item);

        Assert.True(vm.IsEditingItem);
        Assert.Equal(item, vm.EditingTargetItem);
        Assert.True(vm.HasRichText);
        Assert.True(vm.IsRichTextMode);
        Assert.Equal("<b>Hello World</b>", vm.EditingText);
        Assert.Equal(vm.L.GetString("Editor_CharCount", 18), vm.CharCountDisplay);
    }

    [Fact]
    public async Task StartEditItem_NonTextItem_DoesNothing()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        var item = new ClipboardItem
        {
            Id = "img1",
            Type = ClipboardType.Image,
            ImagePath = "test.png"
        };

        vm.StartEditItem(item);

        Assert.False(vm.IsEditingItem);
        Assert.Null(vm.EditingTargetItem);
    }

    [Fact]
    public async Task SwitchEditMode_TogglesBetweenRichTextAndPlainText()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        var item = await history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Plain Text",
            HtmlContent = "<div>HTML Text</div>"
        });

        vm.StartEditItem(item);
        Assert.True(vm.IsRichTextMode);
        Assert.Equal("<div>HTML Text</div>", vm.EditingText);

        // Switch to Plain Text
        vm.SwitchEditMode(false);
        Assert.False(vm.IsRichTextMode);
        Assert.Equal("Plain Text", vm.EditingText);

        // Switch back to Rich Text
        vm.SwitchEditMode(true);
        Assert.True(vm.IsRichTextMode);
        Assert.Equal("<div>HTML Text</div>", vm.EditingText);
    }

    [Fact]
    public async Task SaveEditItemAsync_UpdatesDatabaseItemAndClipboard()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        monitor.TriggerCopy(new ClipboardData { Text = "Original Text" });
        await Task.Delay(20);
        var item = vm.Items.First();

        vm.StartEditItem(item);
        vm.EditingText = "Edited New Text";

        await vm.SaveEditItemAsync();

        Assert.False(vm.IsEditingItem);
        Assert.Equal("Edited New Text", item.Text);
        Assert.Equal("Edited New Text", item.Preview);
        Assert.True(monitor.IsSuppressed);
        Assert.Equal("Edited New Text", pasteService.LastPastedItem?.Text);

        // Verify persisted in DB
        var all = await history.GetAllItemsAsync();
        Assert.Equal("Edited New Text", all.First(x => x.Id == item.Id).Text);
    }

    [Fact]
    public async Task CancelEditItem_ResetsState()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        var item = new ClipboardItem { Id = "t1", Type = ClipboardType.Text, Text = "Some Text" };
        vm.StartEditItem(item);
        Assert.True(vm.IsEditingItem);

        vm.CancelEditItem();
        Assert.False(vm.IsEditingItem);
        Assert.Null(vm.EditingTargetItem);
        Assert.Equal(string.Empty, vm.EditingText);
    }

    [Fact]
    public async Task SaveEditItemAsync_SavedInPlainTextMode_StripsHtmlAndConvertsToPureTextItem()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        var item = await history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Rich Text Plain",
            HtmlContent = "<b>Rich Text Plain</b>"
        });

        vm.StartEditItem(item);
        Assert.True(vm.HasRichText);
        Assert.True(vm.IsRichTextMode);

        // Switch to PlainText mode
        vm.SwitchEditMode(false);
        Assert.False(vm.IsRichTextMode);
        vm.EditingText = "Modified Pure Text";

        // Save in PlainText mode
        await vm.SaveEditItemAsync();

        // Should strip HTML and become a pure text item
        Assert.Equal("Modified Pure Text", item.Text);
        Assert.Null(item.HtmlContent);
        Assert.Null(item.RtfContent);

        // Verify persisted in database
        var all = await history.GetAllItemsAsync();
        var fromDb = all.First(x => x.Id == item.Id);
        Assert.Equal("Modified Pure Text", fromDb.Text);
        Assert.Null(fromDb.HtmlContent);
        Assert.Null(fromDb.RtfContent);
    }

    [Fact]
    public async Task EmptyState_DifferentCategories_ShowSpecificEmptyTextsAndIcons()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();
        var localization = new LocalizationService("zh-CN");

        var vm = new MainViewModel(history, monitor, pasteService, settingsService, localization);
        await vm.InitializeAsync();

        // 1. All category (default)
        Assert.Equal("All", vm.CurrentFilterType);
        Assert.Equal("暂无剪贴板历史", vm.EmptyTitleDisplay);
        Assert.Equal("复制文本、图片或文件后，将自动显示在此处", vm.EmptyHintDisplay);
        Assert.Equal("search", vm.EmptyIconKind);

        // 2. Favorite category
        vm.CurrentFilterType = "Favorite";
        Assert.Equal("暂无收藏记录", vm.EmptyTitleDisplay);
        Assert.Equal("点击条目右侧的星标即可加入收藏", vm.EmptyHintDisplay);
        Assert.Equal("star", vm.EmptyIconKind);

        // 3. Text category
        vm.CurrentFilterType = "Text";
        Assert.Equal("暂无文本记录", vm.EmptyTitleDisplay);
        Assert.Equal("复制文字、代码或链接后，将显示在此处", vm.EmptyHintDisplay);
        Assert.Equal("text", vm.EmptyIconKind);

        // 4. Image category
        vm.CurrentFilterType = "Image";
        Assert.Equal("暂无图片记录", vm.EmptyTitleDisplay);
        Assert.Equal("复制截图或图片后，将显示在此处", vm.EmptyHintDisplay);
        Assert.Equal("image", vm.EmptyIconKind);

        // 5. File category
        vm.CurrentFilterType = "File";
        Assert.Equal("暂无文件记录", vm.EmptyTitleDisplay);
        Assert.Equal("复制文件或文件夹后，将显示在此处", vm.EmptyHintDisplay);
        Assert.Equal("file", vm.EmptyIconKind);

        // 6. Searching in any category overrides to search empty state
        vm.SearchText = "NonExistentKeyword";
        Assert.Equal("未找到相关记录", vm.EmptyTitleDisplay);
        Assert.Equal("尝试使用其他关键词搜索", vm.EmptyHintDisplay);
        Assert.Equal("search", vm.EmptyIconKind);

        // Clear search text restores category empty state
        vm.SearchText = string.Empty;
        Assert.Equal("暂无文件记录", vm.EmptyTitleDisplay);
        Assert.Equal("file", vm.EmptyIconKind);

        // 7. English locale
        localization.SetLanguage("en-US");
        Assert.Equal("No file records", vm.EmptyTitleDisplay);
        Assert.Equal("Copied files or folders will appear here", vm.EmptyHintDisplay);

        vm.CurrentFilterType = "Favorite";
        Assert.Equal("No favorite items", vm.EmptyTitleDisplay);
        Assert.Equal("Star any item to save it to your favorites", vm.EmptyHintDisplay);
    }
}
