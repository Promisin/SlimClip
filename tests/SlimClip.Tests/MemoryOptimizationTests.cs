using System;
using System.IO;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Converters;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class MemoryOptimizationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public MemoryOptimizationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_Mem_Test_" + Guid.NewGuid().ToString("N"));
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
    public async Task ResetState_WhenAlreadyOnDefaultFirstPage_ReusesExistingItemsCollectionWithoutRebuilding()
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
        await Task.Delay(50);

        Assert.Equal(2, vm.Items.Count);
        var originalCollectionRef = vm.Items;
        var originalFirstItemRef = vm.Items[0];

        // Select second item while keeping SearchText="" and CurrentFilterType="All"
        vm.SelectedItem = vm.Items[1];
        Assert.Equal("Item 1", vm.SelectedItem.Text);

        // ResetState should NOT allocate a new ObservableCollection or query DB again
        vm.ResetState();

        Assert.Same(originalCollectionRef, vm.Items);
        Assert.Same(originalFirstItemRef, vm.Items[0]);
        Assert.Same(originalFirstItemRef, vm.SelectedItem);
    }

    [Fact]
    public async Task ResetState_WhenExtraPagesLoaded_TrimsBackToFirstPageInPlace()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        for (int i = 1; i <= 65; i++)
        {
            await history.RecordAsync(new ClipboardData { Text = $"Entry {i}" });
        }

        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();

        var vm = new MainViewModel(history, monitor, pasteService, settingsService);
        await vm.InitializeAsync();

        Assert.Equal(MainViewModel.PageSize, vm.Items.Count);
        var originalCollectionRef = vm.Items;

        await vm.LoadNextPageAsync();
        Assert.Equal(65, vm.Items.Count);

        // ResetState should trim excess pages back to 50 items without throwing away the first 50 items
        vm.ResetState();
        Assert.Equal(MainViewModel.PageSize, vm.Items.Count);
        Assert.Same(originalCollectionRef, vm.Items);
        Assert.True(vm.HasMoreItems);
    }
}
