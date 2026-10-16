using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsClipboardMonitor : IClipboardMonitor
{
    public event EventHandler<ClipboardData>? DataCopied;

    private readonly NativeMethods.WndProcDelegate _wndProcDelegate;
    private IntPtr _hwnd = IntPtr.Zero;
    private uint _lastSequenceNumber;
    private bool _suppressNextCapture;
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private bool _isDisposed;

    private readonly uint _pngFormat;
    private readonly uint _htmlFormat;
    private readonly uint _rtfFormat;

    private string? _className;
    private IntPtr _hInstance;

    public bool IsSuppressed => _suppressNextCapture;

    public WindowsClipboardMonitor()
    {
        _wndProcDelegate = WndProc;
        _pngFormat = NativeMethods.RegisterClipboardFormatW("PNG");
        _htmlFormat = NativeMethods.RegisterClipboardFormatW("HTML Format");
        _rtfFormat = NativeMethods.RegisterClipboardFormatW("Rich Text Format");
    }

    public void Start()
    {
        if (_hwnd != IntPtr.Zero)
            return;

        var className = "SlimClip_MsgWin_" + Guid.NewGuid().ToString("N");
        _className = className;
        _hInstance = NativeMethods.GetModuleHandleW(null);

        var wndClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            lpszClassName = className,
            hInstance = _hInstance
        };

        NativeMethods.RegisterClassExW(ref wndClass);

        _hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            "SlimClipMessageWindow",
            0,
            0, 0, 0, 0,
            (IntPtr)NativeMethods.HWND_MESSAGE,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.AddClipboardFormatListener(_hwnd);
        }
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (!string.IsNullOrEmpty(_className))
        {
            NativeMethods.UnregisterClassW(_className, _hInstance);
            _className = null;
        }
    }

    public void SuppressNextCapture()
    {
        _suppressNextCapture = true;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            HandleClipboardUpdate();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void HandleClipboardUpdate()
    {
        var seq = NativeMethods.GetClipboardSequenceNumber();
        if (seq == _lastSequenceNumber && seq != 0)
            return;
        _lastSequenceNumber = seq;

        if (_suppressNextCapture)
        {
            _suppressNextCapture = false;
            return;
        }

        var now = DateTime.Now;
        if ((now - _lastUpdateTime).TotalMilliseconds < 250)
            return;
        _lastUpdateTime = now;

        _ = Task.Run(() =>
        {
            // Allow external app to finish writing formats
            Thread.Sleep(50);
            ReadAndDispatchClipboard();
        });
    }

    private void ReadAndDispatchClipboard()
    {
        if (!NativeMethods.OpenClipboard(_hwnd))
            return;

        try
        {
            // 1. Files
            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
            {
                var hDrop = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
                if (hDrop != IntPtr.Zero)
                {
                    var filePaths = DecodeFileDropList(hDrop);
                    if (filePaths != null && filePaths.Count > 0)
                    {
                        DataCopied?.Invoke(this, new ClipboardData
                        {
                            Type = ClipboardType.File,
                            FilePaths = filePaths.ToArray()
                        });
                        return;
                    }
                }
            }

            // 2. Images (PNG preferred, then DIB)
            if (_pngFormat != 0 && NativeMethods.IsClipboardFormatAvailable(_pngFormat))
            {
                var hMem = NativeMethods.GetClipboardData(_pngFormat);
                var bytes = ReadGlobalBytes(hMem);
                if (bytes != null && bytes.Length > 0)
                {
                    var (w, h) = PngHelper.ParsePngDimensions(bytes);
                    DataCopied?.Invoke(this, new ClipboardData
                    {
                        Type = ClipboardType.Image,
                        ImageBytes = bytes,
                        ImageWidth = w,
                        ImageHeight = h
                    });
                    return;
                }
            }
            else if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB))
            {
                var hMem = NativeMethods.GetClipboardData(NativeMethods.CF_DIB);
                var dibBytes = ReadGlobalBytes(hMem);
                if (dibBytes != null && dibBytes.Length >= 40)
                {
                    var w = BitConverter.ToInt32(dibBytes, 4);
                    var h = Math.Abs(BitConverter.ToInt32(dibBytes, 8));
                    var pngBytes = ConvertDibToBmpBytes(dibBytes);
                    if (pngBytes != null)
                    {
                        DataCopied?.Invoke(this, new ClipboardData
                        {
                            Type = ClipboardType.Image,
                            ImageBytes = pngBytes,
                            ImageWidth = w,
                            ImageHeight = h
                        });
                        return;
                    }
                }
            }

            // 3. Text & Rich Text
            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
            {
                var hText = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                var text = ReadUnicodeString(hText);

                if (!string.IsNullOrEmpty(text))
                {
                    string? html = null;
                    if (_htmlFormat != 0 && NativeMethods.IsClipboardFormatAvailable(_htmlFormat))
                    {
                        var hHtml = NativeMethods.GetClipboardData(_htmlFormat);
                        html = ReadUtf8String(hHtml);
                    }

                    string? rtf = null;
                    if (_rtfFormat != 0 && NativeMethods.IsClipboardFormatAvailable(_rtfFormat))
                    {
                        var hRtf = NativeMethods.GetClipboardData(_rtfFormat);
                        rtf = ReadAnsiString(hRtf);
                    }

                    DataCopied?.Invoke(this, new ClipboardData
                    {
                        Type = ClipboardType.Text,
                        Text = text,
                        HtmlContent = html,
                        RtfContent = rtf
                    });
                }
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static byte[]? ReadGlobalBytes(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var ptr = NativeMethods.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;

        try
        {
            var size = (int)NativeMethods.GlobalSize(handle).ToUInt32();
            if (size <= 0) return null;
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static string? ReadUnicodeString(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var ptr = NativeMethods.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;

        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static string? ReadUtf8String(IntPtr handle)
    {
        var bytes = ReadGlobalBytes(handle);
        if (bytes == null || bytes.Length == 0) return null;
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static string? ReadAnsiString(IntPtr handle)
    {
        var bytes = ReadGlobalBytes(handle);
        if (bytes == null || bytes.Length == 0) return null;
        return Encoding.Default.GetString(bytes).TrimEnd('\0');
    }

    private static List<string>? DecodeFileDropList(IntPtr hDrop)
    {
        uint count = NativeMethods.DragQueryFileW(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
        if (count == 0) return null;

        var paths = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            uint len = NativeMethods.DragQueryFileW(hDrop, i, IntPtr.Zero, 0);
            if (len == 0) continue;

            var buffer = Marshal.AllocHGlobal((int)(len + 1) * 2);
            try
            {
                NativeMethods.DragQueryFileW(hDrop, i, buffer, len + 1);
                var str = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrEmpty(str))
                    paths.Add(str);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return paths.Count > 0 ? paths : null;
    }

    private static byte[]? ConvertDibToBmpBytes(byte[] dibBytes) => BmpHelper.ConvertDibToBmpBytes(dibBytes);

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
