using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class ClipboardHistoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public ClipboardHistoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "test_slimclip.db");
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
    public async Task RecordAsync_NewText_StoresItemAndReturnsWithPreview()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var data = new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Hello SlimClip!\r\nLine 2\r\nLine 3"
        };

        var item = await history.RecordAsync(data);

        Assert.NotNull(item);
        Assert.Equal(ClipboardType.Text, item.Type);
        Assert.Equal("Hello SlimClip!\r\nLine 2\r\nLine 3", item.Text);
        Assert.Equal($"Hello SlimClip!{Environment.NewLine}Line 2{Environment.NewLine}Line 3", item.Preview);

        var all = await history.GetAllItemsAsync();
        Assert.Single(all);
        Assert.Equal(item.Id, all[0].Id);
    }

    [Fact]
    public async Task RecordAsync_DuplicateText_UpdatesTimestampAndMovesToTop()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var item1 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "First Item" });
        await Task.Delay(20);
        var item2 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Second Item" });
        await Task.Delay(20);

        var allBefore = await history.GetAllItemsAsync();
        Assert.Equal(2, allBefore.Count);
        Assert.Equal(item2.Id, allBefore[0].Id);

        // Record First Item again
        var item1Updated = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "First Item" });
        Assert.Equal(item1.Id, item1Updated.Id);

        var allAfter = await history.GetAllItemsAsync();
        Assert.Equal(2, allAfter.Count);
        // First item should now be at the top!
        Assert.Equal(item1.Id, allAfter[0].Id);
        Assert.True(allAfter[0].CopiedAt > allAfter[1].CopiedAt);
    }

    [Fact]
    public async Task RecordAsync_SameImageBytes_DeduplicatesBySha256()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var dummyPngBytes = Encoding.UTF8.GetBytes("PNG_FAKE_HEADER_1234567890_IMAGE_DATA");
        var data1 = new ClipboardData
        {
            Type = ClipboardType.Image,
            ImageBytes = dummyPngBytes,
            ImageWidth = 100,
            ImageHeight = 200
        };

        var item1 = await history.RecordAsync(data1);
        Assert.NotNull(item1.ImageHash);
        Assert.NotNull(item1.ImagePath);
        Assert.True(File.Exists(Path.Combine(_testDir, item1.ImagePath)));

        await Task.Delay(20);

        // Record same image again
        var item2 = await history.RecordAsync(data1);
        Assert.Equal(item1.Id, item2.Id);
        Assert.Equal(item1.ImageHash, item2.ImageHash);

        var all = await history.GetAllItemsAsync();
        Assert.Single(all);

        // Check image file count in directory
        var imageFiles = Directory.GetFiles(_imagesDir);
        Assert.Single(imageFiles);
    }

    [Fact]
    public async Task TrimExcessAsync_DeletesOldUnfavoritedItems_PreservesFavorited()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var item1 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Item 1" });
        await history.ToggleFavoriteAsync(item1.Id); // Favorited

        for (int i = 2; i <= 6; i++)
        {
            await Task.Delay(10);
            await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = $"Item {i}" });
        }

        var allBefore = await history.GetAllItemsAsync();
        Assert.Equal(6, allBefore.Count);

        // Trim excess with maxItems = 3
        await history.TrimExcessAsync(3);

        var allAfter = await history.GetAllItemsAsync();
        Assert.Contains(allAfter, x => x.Id == item1.Id && x.IsFavorite);
        Assert.True(allAfter.Count <= 4);
    }

    [Fact]
    public async Task CleanPeriodicAsync_DeletesOldItems_PreservesFavorited()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var itemFav = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Favorited Old" });
        await history.ToggleFavoriteAsync(itemFav.Id);

        var itemOld = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Unfavorited Old" });

        // Force item timestamps to be 10 days ago
        var tenDaysAgo = DateTime.Now.AddDays(-10);
        await history.UpdateTimestampAsync(itemFav.Id, tenDaysAgo);
        await history.UpdateTimestampAsync(itemOld.Id, tenDaysAgo);

        var itemNew = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "New Item" });

        // Clean items older than 7 days
        await history.CleanPeriodicAsync(7);

        var remaining = await history.GetAllItemsAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, x => x.Id == itemFav.Id && x.IsFavorite);
        Assert.Contains(remaining, x => x.Id == itemNew.Id);
        Assert.DoesNotContain(remaining, x => x.Id == itemOld.Id);
    }

    [Fact]
    public async Task GetAllItemsAsync_ReturnsStrictChronologicalOrder_RegardlessOfFavorite()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var item1 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Oldest Item" });
        await Task.Delay(20);
        var item2 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Middle Item" });
        await Task.Delay(20);
        var item3 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Latest Item" });

        // Favorite the oldest item
        await history.ToggleFavoriteAsync(item1.Id);

        var all = await history.GetAllItemsAsync();
        Assert.Equal(3, all.Count);
        // The list MUST be strictly chronological: Latest (item3) -> Middle (item2) -> Oldest (item1)
        Assert.Equal(item3.Id, all[0].Id);
        Assert.Equal(item2.Id, all[1].Id);
        Assert.Equal(item1.Id, all[2].Id);
        Assert.True(all[2].IsFavorite);
    }

    [Fact]
    public async Task UpdateItemContentAsync_ValidTextItem_UpdatesContentAndPreviewAndTimestamp()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var item = await history.RecordAsync(new ClipboardData 
        { 
            Type = ClipboardType.Text, 
            Text = "Original Content",
            HtmlContent = "<b>Original</b>" 
        });

        var newTime = DateTime.Now.AddMinutes(5);
        var updated = await history.UpdateItemContentAsync(item.Id, "Modified Content", "<i>Modified</i>", null, newTime);

        Assert.True(updated);

        var all = await history.GetAllItemsAsync();
        var retrieved = all.FirstOrDefault(x => x.Id == item.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Modified Content", retrieved.Text);
        Assert.Equal("Modified Content", retrieved.Preview);
        Assert.Equal("<i>Modified</i>", retrieved.HtmlContent);
        Assert.Null(retrieved.RtfContent);
    }
}
