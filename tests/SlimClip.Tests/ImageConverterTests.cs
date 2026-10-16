using System;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using SlimClip.Core.Models;
using SlimClip.UI.Converters;
using Xunit;

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class ImageConverterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _realLargePngPath;

    public ImageConverterTests()
    {
        StyleTests.EnsureHeadlessApp();
        _tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_ImgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        ImagePathToBitmapConverter.ClearCache();

        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "images");
        if (!Directory.Exists(imagesDir))
        {
            imagesDir = @"D:\Project\SlimClip\images";
        }

        if (Directory.Exists(imagesDir))
        {
            _realLargePngPath = Directory.GetFiles(imagesDir, "*.png").FirstOrDefault();
        }
    }

    public void Dispose()
    {
        ImagePathToBitmapConverter.ClearCache();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void Convert_RealLargePng_DecodesToDownsampledThumbnail()
    {
        if (_realLargePngPath == null || !File.Exists(_realLargePngPath)) return;

        var converter = new ImagePathToBitmapConverter();
        var result = converter.Convert(_realLargePngPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;

        Assert.NotNull(result);
        // Downsampled: width should be bounded <= 360, height <= 220
        Assert.True(result.PixelSize.Width <= 360, $"Expected width <= 360, but got {result.PixelSize.Width}");
        Assert.True(result.PixelSize.Height <= 220, $"Expected height <= 220, but got {result.PixelSize.Height}");
    }

    [Fact]
    public void Convert_SubsequentCalls_ReturnCachedBitmapInstance()
    {
        if (_realLargePngPath == null || !File.Exists(_realLargePngPath)) return;

        var converter = new ImagePathToBitmapConverter();
        var first = converter.Convert(_realLargePngPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;
        var second = converter.Convert(_realLargePngPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first, second);
    }

    [Fact]
    public void Convert_NonExistentPath_ReturnsNull()
    {
        var converter = new ImagePathToBitmapConverter();
        var result = converter.Convert("non_existent_file_12345.png", typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void ClearCache_EmptiesCacheSuccessfully()
    {
        if (_realLargePngPath == null || !File.Exists(_realLargePngPath)) return;

        var converter = new ImagePathToBitmapConverter();
        var first = converter.Convert(_realLargePngPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;
        Assert.NotNull(first);

        ImagePathToBitmapConverter.ClearCache();

        var third = converter.Convert(_realLargePngPath, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;
        Assert.NotNull(third);
        Assert.NotSame(first, third); // Re-decoded after cache clear
    }

    [Fact]
    public void ClipboardItem_ParsedFilePaths_CachesArrayUntilPropertyChanges()
    {
        var item = new ClipboardItem
        {
            Type = ClipboardType.File,
            FilePaths = "[\"C:\\\\test1.txt\", \"C:\\\\test2.txt\"]"
        };

        var first = item.ParsedFilePaths;
        var second = item.ParsedFilePaths;

        Assert.Equal(2, first.Length);
        Assert.Same(first, second);

        // Update property -> cache should be invalidated
        item.FilePaths = "[\"C:\\\\test3.txt\"]";
        var third = item.ParsedFilePaths;

        Assert.Single(third);
        Assert.NotSame(first, third);
        Assert.Equal(@"C:\test3.txt", third[0]);
    }

    [Fact]
    public void ImageHeaderHelper_ValidPng_ReadsCorrectDimensions()
    {
        if (_realLargePngPath == null || !File.Exists(_realLargePngPath)) return;

        using var fs = File.OpenRead(_realLargePngPath);
        var dims = SlimClip.Core.Services.ImageHeaderHelper.TryReadDimensions(fs);

        Assert.NotNull(dims);
        Assert.True(dims.Value.Width > 0);
        Assert.True(dims.Value.Height > 0);
        Assert.Equal(0, fs.Position); // Stream position should be preserved
    }

    [Fact]
    public void ImageHeaderHelper_InvalidData_ReturnsNull()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var dims = SlimClip.Core.Services.ImageHeaderHelper.TryReadDimensions(ms);
        Assert.Null(dims);
    }
}
