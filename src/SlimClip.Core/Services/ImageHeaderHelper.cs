using System;
using System.IO;

namespace SlimClip.Core.Services;

/// <summary>
/// Lightweight helper to probe image dimensions directly from binary headers without decoding full bitmaps.
/// </summary>
public static class ImageHeaderHelper
{
    public static (int Width, int Height)? TryReadDimensions(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryReadDimensions(fs);
        }
        catch
        {
            return null;
        }
    }

    public static (int Width, int Height)? TryReadDimensions(Stream stream)
    {
        try
        {
            if (!stream.CanSeek || stream.Length < 16) return null;

            long originalPos = stream.Position;
            try
            {
                Span<byte> header = stackalloc byte[32];
                int read = stream.Read(header);
                if (read < 16) return null;

                // 1. PNG: 89 50 4E 47 0D 0A 1A 0A
                if (read >= 24 &&
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                    header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                {
                    int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    if (width > 0 && height > 0)
                        return (width, height);
                }

                // 2. BMP: 'B', 'M'
                if (read >= 26 && header[0] == 0x42 && header[1] == 0x4D)
                {
                    int width = header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24);
                    int height = header[22] | (header[23] << 8) | (header[24] << 16) | (header[25] << 24);
                    height = Math.Abs(height);
                    if (width > 0 && height > 0)
                        return (width, height);
                }

                // 3. GIF: 'G', 'I', 'F'
                if (read >= 10 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
                {
                    int width = header[6] | (header[7] << 8);
                    int height = header[8] | (header[9] << 8);
                    if (width > 0 && height > 0)
                        return (width, height);
                }

                // 4. JPEG: 0xFF, 0xD8
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    stream.Position = 2;
                    Span<byte> markerBuf = stackalloc byte[4];
                    while (stream.Position < stream.Length)
                    {
                        int b = stream.ReadByte();
                        if (b == -1) break;
                        if (b != 0xFF) continue;

                        int marker = stream.ReadByte();
                        while (marker == 0xFF)
                        {
                            marker = stream.ReadByte();
                        }
                        if (marker == -1) break;

                        bool isSof = (marker >= 0xC0 && marker <= 0xC3) ||
                                     (marker >= 0xC5 && marker <= 0xC7) ||
                                     (marker >= 0xC9 && marker <= 0xCB) ||
                                     (marker >= 0xCD && marker <= 0xCF);

                        if (isSof)
                        {
                            stream.Seek(3, SeekOrigin.Current);
                            Span<byte> dim = stackalloc byte[4];
                            if (stream.Read(dim) == 4)
                            {
                                int height = (dim[0] << 8) | dim[1];
                                int width = (dim[2] << 8) | dim[3];
                                if (width > 0 && height > 0)
                                    return (width, height);
                            }
                            break;
                        }
                        else if (marker == 0xDA || marker == 0xD9)
                        {
                            break;
                        }
                        else
                        {
                            if (stream.Read(markerBuf.Slice(0, 2)) != 2) break;
                            int len = (markerBuf[0] << 8) | markerBuf[1];
                            if (len < 2) break;
                            stream.Seek(len - 2, SeekOrigin.Current);
                        }
                    }
                }
            }
            finally
            {
                stream.Position = originalPos;
            }
        }
        catch
        {
        }

        return null;
    }
}
