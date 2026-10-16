using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class ClipboardHistoryPagingTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _imagesDir;

    public ClipboardHistoryPagingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SlimClip_PagingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "test_paged_slimclip.db");
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
    public async Task GetPagedItemsAsync_SlimProjection_ExcludesHtmlAndRtfContent()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        // 插入包含大体积 HTML 和 RTF 的记录
        await history.RecordAsync(new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "Plain Text Snippet",
            HtmlContent = "<p style='font-size:24px'>Heavy Rich HTML Document Body</p>",
            RtfContent = "{\\rtf1\\ansi\\ansicpg1252\\cocoartf2709 Heavy RTF}"
        });

        // 验证 GetPagedItemsAsync 投影不读取大文本字段 (Slim Projection)
        var paged = await history.GetPagedItemsAsync(limit: 10, offset: 0);
        Assert.Single(paged);
        var item = paged[0];
        Assert.Equal("Plain Text Snippet", item.Text);
        Assert.Null(item.HtmlContent);
        Assert.Null(item.RtfContent);

        // 验证通过 GetItemContentAsync 按需可拉取完整富文本
        var (html, rtf) = await history.GetItemContentAsync(item.Id);
        Assert.Equal("<p style='font-size:24px'>Heavy Rich HTML Document Body</p>", html);
        Assert.Equal("{\\rtf1\\ansi\\ansicpg1252\\cocoartf2709 Heavy RTF}", rtf);
    }

    [Fact]
    public async Task GetPagedItemsAsync_PaginationLimitOffset_ReturnsAccurateChunks()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        for (int i = 1; i <= 5; i++)
        {
            await history.RecordAsync(new ClipboardData
            {
                Type = ClipboardType.Text,
                Text = $"Record #{i}"
            });
            await Task.Delay(15);
        }

        // 数据库总共 5 条，按照时间倒序：#5, #4, #3, #2, #1
        var all = await history.GetAllItemsAsync();
        Assert.Equal(5, all.Count);

        // Page 1: limit 2, offset 0 -> #5, #4
        var page1 = await history.GetPagedItemsAsync(limit: 2, offset: 0);
        Assert.Equal(2, page1.Count);
        Assert.Equal("Record #5", page1[0].Text);
        Assert.Equal("Record #4", page1[1].Text);

        // Page 2: limit 2, offset 2 -> #3, #2
        var page2 = await history.GetPagedItemsAsync(limit: 2, offset: 2);
        Assert.Equal(2, page2.Count);
        Assert.Equal("Record #3", page2[0].Text);
        Assert.Equal("Record #2", page2[1].Text);

        // Page 3: limit 2, offset 4 -> #1
        var page3 = await history.GetPagedItemsAsync(limit: 2, offset: 4);
        Assert.Single(page3);
        Assert.Equal("Record #1", page3[0].Text);

        // Page 4: limit 2, offset 6 -> empty
        var page4 = await history.GetPagedItemsAsync(limit: 2, offset: 6);
        Assert.Empty(page4);
    }

    [Fact]
    public async Task GetPagedItemsAsync_TypeAndFavoriteFilter_FiltersCorrectlyInDatabase()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        var itemText1 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Text Item A" });
        await Task.Delay(10);
        var itemText2 = await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Text Item B" });
        await Task.Delay(10);
        var itemFile = await history.RecordAsync(new ClipboardData { Type = ClipboardType.File, Text = "File Item C", FilePaths = new[] { "C:\\file.txt" } });

        // 将 itemText1 设为收藏
        await history.ToggleFavoriteAsync(itemText1.Id);

        // 1. 过滤收藏
        var favorites = await history.GetPagedItemsAsync(limit: 10, offset: 0, favoriteOnly: true);
        Assert.Single(favorites);
        Assert.Equal(itemText1.Id, favorites[0].Id);

        // 2. 过滤类型 (File)
        var files = await history.GetPagedItemsAsync(limit: 10, offset: 0, type: ClipboardType.File);
        Assert.Single(files);
        Assert.Equal(itemFile.Id, files[0].Id);

        // 3. 组合过滤：类型 Text 且 Favorite
        var favTexts = await history.GetPagedItemsAsync(limit: 10, offset: 0, type: ClipboardType.Text, favoriteOnly: true);
        Assert.Single(favTexts);
        Assert.Equal(itemText1.Id, favTexts[0].Id);
    }

    [Fact]
    public async Task GetPagedItemsAsync_SearchTextFilter_MatchesTextOrPreview()
    {
        using var history = new ClipboardHistory(_dbPath, _imagesDir);
        await history.InitializeAsync();

        await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Quick brown fox" });
        await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Lazy dog jumps" });
        await history.RecordAsync(new ClipboardData { Type = ClipboardType.Text, Text = "Fox in the forest" });

        var results = await history.GetPagedItemsAsync(limit: 10, offset: 0, searchText: "fox");
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("fox", r.Text, StringComparison.OrdinalIgnoreCase));

        var dogResults = await history.GetPagedItemsAsync(limit: 10, offset: 0, searchText: "dog");
        Assert.Single(dogResults);
    }
}
