using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

internal static class WindowsImageFormatHelper
{
    private static readonly UIntPtr _gdiToken;
    private static readonly Guid BmpEncoderGuid = new("557cf400-1a04-11d3-9a73-0000f81ef32e");
    private static readonly Guid PngEncoderGuid = new("557cf406-1a04-11d3-9a73-0000f81ef32e");

    static WindowsImageFormatHelper()
    {
        var input = NativeMethods.GdiplusStartupInput.Default;
        NativeMethods.GdiplusStartup(out _gdiToken, ref input, IntPtr.Zero);
    }

    public static byte[]? ConvertImageToDibBytes(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;

        // 如果本身就是 BMP 格式，前 14 字节为 BITMAPFILEHEADER，随后即为 CF_DIB 结构
        if (BmpHelper.ConvertBmpToDibBytes(imageBytes) is { Length: > 0 } dib)
        {
            return dib;
        }

        // 其它图像格式（如 PNG/JPEG），通过 GDI+ 转为标准 BMP 流后剥离文件头
        return ConvertViaGdiPlus(imageBytes, BmpEncoderGuid, extractDib: true);
    }

    public static byte[]? ConvertImageToPngBytes(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;

        // 如果已经是标准 PNG 魔数 (\x89PNG)
        if (imageBytes.Length > 8 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
        {
            return imageBytes;
        }

        // 其它格式（如 BMP/DIB）转为标准 PNG 字节流
        return ConvertViaGdiPlus(imageBytes, PngEncoderGuid, extractDib: false);
    }

    private static byte[]? ConvertViaGdiPlus(byte[] imageBytes, Guid encoderGuid, bool extractDib)
    {
        var hMem = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)imageBytes.Length);
        if (hMem == IntPtr.Zero) return null;

        var ptr = NativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hMem);
            return null;
        }

        Marshal.Copy(imageBytes, 0, ptr, imageBytes.Length);
        NativeMethods.GlobalUnlock(hMem);

        if (NativeMethods.CreateStreamOnHGlobal(hMem, true, out var inStream) != 0)
        {
            NativeMethods.GlobalFree(hMem);
            return null;
        }

        IntPtr image = IntPtr.Zero;
        try
        {
            if (NativeMethods.GdipCreateBitmapFromStream(inStream, out image) != 0 || image == IntPtr.Zero)
            {
                return null;
            }

            if (NativeMethods.CreateStreamOnHGlobal(IntPtr.Zero, true, out var outStream) != 0)
            {
                return null;
            }

            try
            {
                if (NativeMethods.GdipSaveImageToStream(image, outStream, ref encoderGuid, IntPtr.Zero) != 0)
                {
                    return null;
                }

                if (NativeMethods.GetHGlobalFromStream(outStream, out var hOutGlobal) == 0 && hOutGlobal != IntPtr.Zero)
                {
                    var outPtr = NativeMethods.GlobalLock(hOutGlobal);
                    if (outPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var size = (int)NativeMethods.GlobalSize(hOutGlobal);
                            if (extractDib)
                            {
                                if (size > 14)
                                {
                                    var dib = new byte[size - 14];
                                    Marshal.Copy(IntPtr.Add(outPtr, 14), dib, 0, dib.Length);
                                    return dib;
                                }
                                return null;
                            }
                            else
                            {
                                var outBytes = new byte[size];
                                Marshal.Copy(outPtr, outBytes, 0, size);
                                return outBytes;
                            }
                        }
                        finally
                        {
                            NativeMethods.GlobalUnlock(hOutGlobal);
                        }
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(outStream);
            }
        }
        finally
        {
            if (image != IntPtr.Zero)
            {
                NativeMethods.GdipDisposeImage(image);
            }
            Marshal.ReleaseComObject(inStream);
        }

        return null;
    }
}
