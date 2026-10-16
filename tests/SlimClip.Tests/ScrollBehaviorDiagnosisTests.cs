using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using SlimClip.UI.Views;
using Xunit;
using Xunit.Abstractions;

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class ScrollBehaviorDiagnosisTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public ScrollBehaviorDiagnosisTests(ITestOutputHelper output)
    {
        _output = output;
        StyleTests.EnsureHeadlessApp();
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_Diag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "test.db");
        _imagesDir = Path.Combine(_testDir, "images");
        Directory.CreateDirectory(_imagesDir);
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

    private MainViewModel CreateViewModelWithRealHistory(int count)
    {
        return Task.Run(async () =>
        {
            var history = new ClipboardHistory(_dbPath, _imagesDir);
            await history.InitializeAsync();

            for (int i = 0; i < count; i++)
            {
                var data = new ClipboardData
                {
                    Type = ClipboardType.Text,
                    Text = $"History Item #{i:D3} - " + (i % 3 == 0 ? "Multi\nLine\nContent" : "Single line text")
                };
                await history.RecordAsync(data);
            }

            var monitor = new FakeClipboardMonitor();
            var pasteService = new FakePasteService();
            var settingsService = new SettingsService(null, _testDir);

            var vm = new MainViewModel(history, monitor, pasteService, settingsService);
            await vm.InitializeAsync();
            return vm;
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public void ShowPanel_AfterScrollingDownAndHiding_MustResetScrollOffsetToTop()
    {
        var vm = CreateViewModelWithRealHistory(60);
        var panel = new ClipboardPanel(vm);
        panel.Show();
        panel.UpdateLayout();

        var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()!;
        Assert.NotNull(scrollViewer);

        // 1. Simulate user scrolling down
        scrollViewer.Offset = new Vector(0, 500);
        panel.UpdateLayout();
        _output.WriteLine($"[DIAG-scroll] Initial scroll offset: {scrollViewer.Offset.Y}");
        Assert.True(scrollViewer.Offset.Y > 0, "ScrollViewer should be scrolled down");

        // 2. Simulate user closing the window
        panel.Dismiss();
        panel.UpdateLayout();

        // 3. Simulate user reopening the window via hotkey
        panel.ShowPanel();
        panel.UpdateLayout();

        _output.WriteLine($"[DIAG-scroll] Offset after ShowPanel: {scrollViewer.Offset.Y}");
        _output.WriteLine($"[DIAG-scroll] SelectedItem: {vm.SelectedItem?.Text}");

        // The scroll position MUST be at the top (0), not left at previous scrolled position!
        Assert.Equal(0, scrollViewer.Offset.Y);

        panel.Close();
    }

    [Fact]
    public void ItemsListBox_UsesStackPanel_ToPreventVirtualizationJumps()
    {
        var vm = CreateViewModelWithRealHistory(20);
        var panel = new ClipboardPanel(vm);
        panel.Show();
        panel.UpdateLayout();

        var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
        Assert.NotNull(listBox);
        Assert.False(listBox.AutoScrollToSelectedItem);

        var itemsPresenter = listBox.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ItemsPresenter>().FirstOrDefault();
        Assert.NotNull(itemsPresenter);
        Assert.IsType<StackPanel>(itemsPresenter.Panel);

        panel.Close();
    }

    [Fact]
    public void FilterChange_TriggersRequestScrollToHome_AndScrollsToTop()
    {
        var vm = CreateViewModelWithRealHistory(60);
        var panel = new ClipboardPanel(vm);
        panel.Show();
        panel.UpdateLayout();

        var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()!;
        Assert.NotNull(scrollViewer);

        // 1. 模拟用户滚动到下方
        scrollViewer.Offset = new Vector(0, 400);
        panel.UpdateLayout();
        Assert.True(scrollViewer.Offset.Y > 0);

        // 2. 切换分类
        vm.CurrentFilterType = "Text";

        // 等待异步刷新完成并泵送 Dispatcher 任务
        System.Threading.Thread.Sleep(100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        panel.UpdateLayout();

        Assert.Equal(0, scrollViewer.Offset.Y);
        Assert.NotNull(vm.SelectedItem);

        panel.Close();
    }

    [Fact]
    public void DiagnoseScrollUpBehavior_MustNotSnapBackToTop()
    {
        var vm = CreateViewModelWithRealHistory(60);
        var panel = new ClipboardPanel(vm);
        panel.Show();
        panel.UpdateLayout();

        var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()!;
        Assert.NotNull(scrollViewer);

        // 模拟用户向下滚动 500px（图 1 状态，SelectedItem 依然是第 0 项）
        scrollViewer.Offset = new Vector(0, 500);
        panel.UpdateLayout();
        Assert.True(scrollViewer.Offset.Y >= 400, "Scroll should be at scrolled-down position");

        // 模拟向上滚动一格（例如用户向上拨动一格鼠标滚轮，减少 48px）
        scrollViewer.Offset = new Vector(0, Math.Max(0, scrollViewer.Offset.Y - 48));
        panel.UpdateLayout();

        _output.WriteLine($"[DIAG-scroll] After 1 step scroll up -> Offset: {scrollViewer.Offset}");
        // 关键断言：绝对不能跳回顶部 (Offset.Y == 0)，必须连续稳定地保持在 ~452px！
        Assert.True(scrollViewer.Offset.Y > 400, $"Offset should remain near ~452px instead of snapping to 0, actual: {scrollViewer.Offset.Y}");

        panel.Close();
    }
}
