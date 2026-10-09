using System;
using System.IO;
using System.Text.Json;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class UtilityTests
{
    [Theory]
    [InlineData("#FFF", true, 255, 255, 255, 255)]
    [InlineData("#F00", true, 255, 255, 0, 0)]
    [InlineData("#FF0000", true, 255, 255, 0, 0)]
    [InlineData("#123456", true, 255, 18, 52, 86)]
    [InlineData("#80123456", true, 128, 18, 52, 86)]
    [InlineData("not a color", false, 0, 0, 0, 0)]
    [InlineData("#12", false, 0, 0, 0, 0)]
    [InlineData("#GGGGGG", false, 0, 0, 0, 0)]
    public void HexColorParser_Parse_ValidatesCorrectly(string input, bool expectedValid, byte a, byte r, byte g, byte b)
    {
        var isValid = HexColorParser.TryParse(input, out var color);
        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.Equal(a, color.A);
            Assert.Equal(r, color.R);
            Assert.Equal(g, color.G);
            Assert.Equal(b, color.B);
        }
    }

    [Fact]
    public void FileExistenceValidator_DetectsMissingFiles()
    {
        var tempFile = Path.GetTempFileName();
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var files = new[] { tempFile, nonExistent };
            var (existing, missing) = FileExistenceValidator.Partition(files);

            Assert.Single(existing);
            Assert.Equal(tempFile, existing[0]);

            Assert.Single(missing);
            Assert.Equal(nonExistent, missing[0]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void FileExistenceValidator_NormalizesFileUrl()
    {
        var url = "file:///Users/test/Documents/report.pdf";
        var normalized = FileExistenceValidator.NormalizePath(url);
        Assert.Equal("/Users/test/Documents/report.pdf", normalized.Replace('\\', '/'));
    }

    [Fact]
    public void PngHelper_ParsePngDimensions_ParsesValidHeader()
    {
        // 24 bytes minimum for PNG header: 8 byte signature, 4 byte chunk length, 4 byte chunk type (IHDR), 4 byte width, 4 byte height
        var png = new byte[24];
        // Width 1920 (0x00000780) at offset 16
        png[16] = 0x00;
        png[17] = 0x00;
        png[18] = 0x07;
        png[19] = 0x80;
        // Height 1080 (0x00000438) at offset 20
        png[20] = 0x00;
        png[21] = 0x00;
        png[22] = 0x04;
        png[23] = 0x38;

        var (w, h) = PngHelper.ParsePngDimensions(png);
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
    }

    [Theory]
    [InlineData(Avalonia.Input.Key.V, 0x56)] // 'V'
    [InlineData(Avalonia.Input.Key.C, 0x43)] // 'C'
    [InlineData(Avalonia.Input.Key.A, 0x41)] // 'A'
    [InlineData(Avalonia.Input.Key.F1, 0x70)] // VK_F1
    [InlineData(Avalonia.Input.Key.Space, 0x20)]
    public void KeyMapper_AvaloniaKeyToWindowsVk_MapsAccurately(Avalonia.Input.Key key, int expectedVk)
    {
        var vk = SlimClip.UI.Converters.KeyMapper.AvaloniaKeyToWindowsVk(key);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData(Avalonia.Input.Key.V, 9)] // kVK_ANSI_V
    [InlineData(Avalonia.Input.Key.C, 8)] // kVK_ANSI_C
    [InlineData(Avalonia.Input.Key.A, 0)] // kVK_ANSI_A
    public void KeyMapper_AvaloniaKeyToMacVirtualKey_MapsAccurately(Avalonia.Input.Key key, int expectedMacKey)
    {
        var macKey = SlimClip.UI.Converters.KeyMapper.AvaloniaKeyToMacVirtualKey(key);
        Assert.Equal(expectedMacKey, macKey);
    }

    [Fact]
    public void ClipboardItem_SingleImageFile_HasImagePreviewAndEffectiveImagePath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_preview_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });

        try
        {
            var item = new ClipboardItem
            {
                Type = ClipboardType.File,
                FilePaths = System.Text.Json.JsonSerializer.Serialize(new[] { tempFile }),
                Text = tempFile
            };

            Assert.True(item.IsFile);
            Assert.True(item.IsSingleImageFile);
            Assert.True(item.HasImagePreview);
            Assert.Equal(tempFile, item.EffectiveImagePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ClipboardItem_NonImageFileOrMultipleFiles_DoesNotHaveImagePreview()
    {
        var tempTxt = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.txt");
        var tempPng1 = Path.Combine(Path.GetTempPath(), $"test_1_{Guid.NewGuid():N}.png");
        var tempPng2 = Path.Combine(Path.GetTempPath(), $"test_2_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempTxt, new byte[] { 1, 2 });
        File.WriteAllBytes(tempPng1, new byte[] { 1, 2 });
        File.WriteAllBytes(tempPng2, new byte[] { 1, 2 });

        try
        {
            // Non-image file
            var itemTxt = new ClipboardItem
            {
                Type = ClipboardType.File,
                FilePaths = System.Text.Json.JsonSerializer.Serialize(new[] { tempTxt }),
                Text = tempTxt
            };
            Assert.False(itemTxt.IsSingleImageFile);
            Assert.False(itemTxt.HasImagePreview);
            Assert.Null(itemTxt.EffectiveImagePath);

            // Multiple files
            var itemMulti = new ClipboardItem
            {
                Type = ClipboardType.File,
                FilePaths = System.Text.Json.JsonSerializer.Serialize(new[] { tempPng1, tempPng2 }),
                Text = $"{tempPng1}\r\n{tempPng2}"
            };
            Assert.False(itemMulti.IsSingleImageFile);
            Assert.False(itemMulti.HasImagePreview);
            Assert.Null(itemMulti.EffectiveImagePath);
        }
        finally
        {
            if (File.Exists(tempTxt)) File.Delete(tempTxt);
            if (File.Exists(tempPng1)) File.Delete(tempPng1);
            if (File.Exists(tempPng2)) File.Delete(tempPng2);
        }
    }

    [Fact]
    public void ClipboardItem_Image_HasImagePreviewAndEffectiveImagePath()
    {
        var item = new ClipboardItem
        {
            Type = ClipboardType.Image,
            ImagePath = "images/abc.png"
        };

        Assert.True(item.IsImage);
        Assert.True(item.HasImagePreview);
        Assert.Equal("images/abc.png", item.EffectiveImagePath);
    }

    [Fact]
    public void ImagePathToBitmapConverter_ResolveImagePath_HandlesScenarios()
    {
        // Null or whitespace returns null
        Assert.Null(SlimClip.UI.Converters.ImagePathToBitmapConverter.ResolveImagePath(""));
        Assert.Null(SlimClip.UI.Converters.ImagePathToBitmapConverter.ResolveImagePath("   "));

        // Non-existent path returns null
        Assert.Null(SlimClip.UI.Converters.ImagePathToBitmapConverter.ResolveImagePath(@"C:\non_existent_folder_xyz\img.png"));

        // Direct rooted path that exists
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_resolve_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });
        try
        {
            var resolved = SlimClip.UI.Converters.ImagePathToBitmapConverter.ResolveImagePath(tempFile);
            Assert.Equal(tempFile, resolved);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void BmpHelper_ConvertDibToBmpBytes_BiBitfields_ComputesCorrectOffset66()
    {
        // 40 bytes header + 12 bytes masks (RGB) + 4 bytes pixel data = 56 bytes
        var dib = new byte[56];
        BitConverter.GetBytes(40).CopyTo(dib, 0); // biSize = 40
        BitConverter.GetBytes(1).CopyTo(dib, 4);  // biWidth = 1
        BitConverter.GetBytes(1).CopyTo(dib, 8);  // biHeight = 1
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12); // biPlanes = 1
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14); // biBitCount = 32
        BitConverter.GetBytes(3).CopyTo(dib, 16); // biCompression = 3 (BI_BITFIELDS)

        var bmp = BmpHelper.ConvertDibToBmpBytes(dib);
        Assert.NotNull(bmp);
        Assert.Equal(0x42, bmp[0]); // 'B'
        Assert.Equal(0x4D, bmp[1]); // 'M'
        int offset = BitConverter.ToInt32(bmp, 10);
        // 14 (BMP header) + 40 (biSize) + 12 (BI_BITFIELDS masks) = 66
        Assert.Equal(66, offset);
        Assert.Equal(14 + dib.Length, bmp.Length);
    }

    [Fact]
    public void BmpHelper_ConvertDibToBmpBytes_BiRgb_ComputesOffset54()
    {
        // 40 bytes header + 4 bytes pixel data = 44 bytes
        var dib = new byte[44];
        BitConverter.GetBytes(40).CopyTo(dib, 0); // biSize = 40
        BitConverter.GetBytes(1).CopyTo(dib, 4);  // biWidth = 1
        BitConverter.GetBytes(1).CopyTo(dib, 8);  // biHeight = 1
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12); // biPlanes = 1
        BitConverter.GetBytes((ushort)24).CopyTo(dib, 14); // biBitCount = 24
        BitConverter.GetBytes(0).CopyTo(dib, 16); // biCompression = 0 (BI_RGB)

        var bmp = BmpHelper.ConvertDibToBmpBytes(dib);
        Assert.NotNull(bmp);
        int offset = BitConverter.ToInt32(bmp, 10);
        // 14 (BMP header) + 40 (biSize) + 0 = 54
        Assert.Equal(54, offset);
    }

    [Fact]
    public void BmpHelper_ConvertDibToBmpBytes_NullOrTooShort_ReturnsNull()
    {
        Assert.Null(BmpHelper.ConvertDibToBmpBytes(null!));
        Assert.Null(BmpHelper.ConvertDibToBmpBytes(new byte[39]));
    }
}
