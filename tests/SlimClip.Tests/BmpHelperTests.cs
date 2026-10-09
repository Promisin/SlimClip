using System;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class BmpHelperTests
{
    [Fact]
    public void ConvertDibToBmp_And_ConvertBmpToDib_RoundTripSuccessfully()
    {
        // Construct a minimal valid 40-byte BITMAPINFOHEADER DIB
        var dib = new byte[40 + 16]; // 40 header + 16 pixels
        BitConverter.GetBytes(40).CopyTo(dib, 0); // biSize
        BitConverter.GetBytes(2).CopyTo(dib, 4);  // biWidth = 2
        BitConverter.GetBytes(2).CopyTo(dib, 8);  // biHeight = 2
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12); // biPlanes
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14); // biBitCount = 32bpp

        // Fill test pixels
        for (int i = 40; i < dib.Length; i++)
        {
            dib[i] = (byte)(i * 3);
        }

        // Convert DIB -> BMP
        var bmp = BmpHelper.ConvertDibToBmpBytes(dib);
        Assert.NotNull(bmp);
        Assert.Equal(14 + dib.Length, bmp.Length);
        Assert.Equal(0x42, bmp[0]); // 'B'
        Assert.Equal(0x4D, bmp[1]); // 'M'

        // Convert BMP -> DIB
        var roundTripDib = BmpHelper.ConvertBmpToDibBytes(bmp);
        Assert.NotNull(roundTripDib);
        Assert.Equal(dib.Length, roundTripDib.Length);
        Assert.Equal(dib, roundTripDib);
    }

    [Fact]
    public void ConvertBmpToDib_ReturnsNull_ForInvalidData()
    {
        Assert.Null(BmpHelper.ConvertBmpToDibBytes(null!));
        Assert.Null(BmpHelper.ConvertBmpToDibBytes(new byte[10])); // too short
        Assert.Null(BmpHelper.ConvertBmpToDibBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })); // PNG magic, not BM
    }
}
