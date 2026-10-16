using System;

namespace SlimClip.Core.Services;

public static class BmpHelper
{
    public static byte[]? ConvertDibToBmpBytes(byte[] dibBytes)
    {
        if (dibBytes == null || dibBytes.Length < 40) return null;

        int biSize = BitConverter.ToInt32(dibBytes, 0);
        int biWidth = BitConverter.ToInt32(dibBytes, 4);
        int biHeight = BitConverter.ToInt32(dibBytes, 8);
        ushort biBitCount = BitConverter.ToUInt16(dibBytes, 14);
        int biCompression = dibBytes.Length >= 20 ? BitConverter.ToInt32(dibBytes, 16) : 0;

        int colors = 0;
        if (dibBytes.Length >= 36)
        {
            int biClrUsed = BitConverter.ToInt32(dibBytes, 32);
            if (biClrUsed > 0)
            {
                colors = biClrUsed;
            }
            else if (biBitCount <= 8 && biBitCount > 0)
            {
                colors = 1 << biBitCount;
            }
        }
        else if (biBitCount <= 8 && biBitCount > 0)
        {
            colors = 1 << biBitCount;
        }

        int maskSize = 0;
        // In standard BITMAPINFOHEADER (biSize == 40), BI_BITFIELDS uses 3 DWORD color masks (12 bytes),
        // and BI_ALPHABITFIELDS uses 4 DWORD masks (16 bytes) that follow the header before the bitmap bits.
        if (biSize == 40)
        {
            if (biCompression == 3) // BI_BITFIELDS
            {
                maskSize = 12;
            }
            else if (biCompression == 6) // BI_ALPHABITFIELDS
            {
                maskSize = 16;
            }
        }

        int offset = 14 + biSize + maskSize + (colors * 4);
        byte[] bmp = new byte[14 + dibBytes.Length];

        // BM header
        bmp[0] = 0x42; // 'B'
        bmp[1] = 0x4D; // 'M'
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(offset).CopyTo(bmp, 10);

        Array.Copy(dibBytes, 0, bmp, 14, dibBytes.Length);
        return bmp;
    }

    public static byte[]? ConvertBmpToDibBytes(byte[] bmpBytes)
    {
        if (bmpBytes == null || bmpBytes.Length <= 14) return null;
        if (bmpBytes[0] != 0x42 || bmpBytes[1] != 0x4D) return null;

        var dib = new byte[bmpBytes.Length - 14];
        Buffer.BlockCopy(bmpBytes, 14, dib, 0, dib.Length);
        return dib;
    }
}
