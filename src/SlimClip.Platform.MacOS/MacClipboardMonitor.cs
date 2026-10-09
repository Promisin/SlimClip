using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacClipboardMonitor : IClipboardMonitor
{
    public event EventHandler<ClipboardData>? DataCopied;

    private Timer? _pollingTimer;
    private long _lastChangeCount;
    private bool _suppressNextCapture;
    private bool _isDisposed;

    public bool IsSuppressed => _suppressNextCapture;

    public void Start()
    {
        if (_pollingTimer != null)
            return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        _lastChangeCount = GetChangeCount();
        _pollingTimer = new Timer(OnPoll, null, 250, 250);
    }

    public void Stop()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    public void SuppressNextCapture()
    {
        _suppressNextCapture = true;
    }

    private void OnPoll(object? state)
    {
        try
        {
            var count = GetChangeCount();
            if (count == _lastChangeCount)
                return;

            _lastChangeCount = count;

            if (_suppressNextCapture)
            {
                _suppressNextCapture = false;
                return;
            }

            ReadAndDispatchClipboard();
        }
        catch { }
    }

    private static long GetChangeCount()
    {
        try
        {
            var pboardCls = MacNativeMethods.objc_getClass("NSPasteboard");
            var genSel = MacNativeMethods.sel_registerName("generalPasteboard");
            var pboard = MacNativeMethods.objc_msgSend(pboardCls, genSel);

            var countSel = MacNativeMethods.sel_registerName("changeCount");
            return MacNativeMethods.objc_msgSend_long(pboard, countSel);
        }
        catch
        {
            return 0;
        }
    }

    private void ReadAndDispatchClipboard()
    {
        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var pboardCls = MacNativeMethods.objc_getClass("NSPasteboard");
            var genSel = MacNativeMethods.sel_registerName("generalPasteboard");
            var pboard = MacNativeMethods.objc_msgSend(pboardCls, genSel);

            // 1. Check Files: NSFilenamesPboardType (supports multiple files)
            var filenamesType = MacNativeMethods.CreateNSString("NSFilenamesPboardType");
            var plistForTypeSel = MacNativeMethods.sel_registerName("propertyListForType:");
            var nsPlist = MacNativeMethods.objc_msgSend(pboard, plistForTypeSel, filenamesType);
            if (nsPlist != IntPtr.Zero)
            {
                var countSel = MacNativeMethods.sel_registerName("count");
                var count = (int)MacNativeMethods.objc_msgSend_long(nsPlist, countSel);
                if (count > 0)
                {
                    var objectAtSel = MacNativeMethods.sel_registerName("objectAtIndex:");
                    var fileList = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var nsStr = MacNativeMethods.objc_msgSend(nsPlist, objectAtSel, (IntPtr)i);
                        var path = MacNativeMethods.ReadNSString(nsStr);
                        if (!string.IsNullOrEmpty(path))
                        {
                            fileList.Add(FileExistenceValidator.NormalizePath(path));
                        }
                    }

                    if (fileList.Count > 0)
                    {
                        DataCopied?.Invoke(this, new ClipboardData
                        {
                            Type = ClipboardType.File,
                            FilePaths = fileList.ToArray()
                        });
                        return;
                    }
                }
            }

            // Fallback for single file-url
            var fileUrlType = MacNativeMethods.CreateNSString("public.file-url");
            var stringForTypeSel = MacNativeMethods.sel_registerName("stringForType:");
            var nsFileUrl = MacNativeMethods.objc_msgSend(pboard, stringForTypeSel, fileUrlType);
            var fileUrl = MacNativeMethods.ReadNSString(nsFileUrl);
            if (!string.IsNullOrEmpty(fileUrl))
            {
                var normalized = FileExistenceValidator.NormalizePath(fileUrl);
                DataCopied?.Invoke(this, new ClipboardData
                {
                    Type = ClipboardType.File,
                    FilePaths = new[] { normalized }
                });
                return;
            }

            // 2. Check PNG data (or fallback to TIFF)
            var dataForTypeSel = MacNativeMethods.sel_registerName("dataForType:");
            var pngType = MacNativeMethods.CreateNSString("public.png");
            var nsPngData = MacNativeMethods.objc_msgSend(pboard, dataForTypeSel, pngType);

            byte[]? imgBytes = null;
            if (nsPngData != IntPtr.Zero)
            {
                imgBytes = ReadDataBytes(nsPngData);
            }
            else
            {
                // Fallback: Check TIFF (macOS native screenshot / Cocoa standard format)
                var tiffType = MacNativeMethods.CreateNSString("public.tiff");
                var nsTiffData = MacNativeMethods.objc_msgSend(pboard, dataForTypeSel, tiffType);
                if (nsTiffData != IntPtr.Zero)
                {
                    imgBytes = ConvertTiffToPng(nsTiffData);
                }
            }

            if (imgBytes != null && imgBytes.Length > 0)
            {
                var (w, h) = PngHelper.ParsePngDimensions(imgBytes);

                DataCopied?.Invoke(this, new ClipboardData
                {
                    Type = ClipboardType.Image,
                    ImageBytes = imgBytes,
                    ImageWidth = w,
                    ImageHeight = h
                });
                return;
            }

            // 3. Check Text, HTML, RTF
            var strType = MacNativeMethods.CreateNSString("public.utf8-plain-text");
            var nsString = MacNativeMethods.objc_msgSend(pboard, stringForTypeSel, strType);
            var text = MacNativeMethods.ReadNSString(nsString);

            var htmlType = MacNativeMethods.CreateNSString("public.html");
            var nsHtml = MacNativeMethods.objc_msgSend(pboard, stringForTypeSel, htmlType);
            var html = MacNativeMethods.ReadNSString(nsHtml);

            var rtfType = MacNativeMethods.CreateNSString("public.rtf");
            var nsRtf = MacNativeMethods.objc_msgSend(pboard, stringForTypeSel, rtfType);
            var rtf = MacNativeMethods.ReadNSString(nsRtf);

            if (!string.IsNullOrEmpty(text))
            {
                DataCopied?.Invoke(this, new ClipboardData
                {
                    Type = ClipboardType.Text,
                    Text = text,
                    HtmlContent = html,
                    RtfContent = rtf
                });
            }
        }
        catch { }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }

    private static byte[]? ReadDataBytes(IntPtr nsData)
    {
        if (nsData == IntPtr.Zero) return null;
        var lenSel = MacNativeMethods.sel_registerName("length");
        var len = (int)MacNativeMethods.objc_msgSend_long(nsData, lenSel);
        if (len <= 0) return null;

        var bytesSel = MacNativeMethods.sel_registerName("bytes");
        var bytesPtr = MacNativeMethods.objc_msgSend(nsData, bytesSel);
        if (bytesPtr == IntPtr.Zero) return null;

        var bytes = new byte[len];
        Marshal.Copy(bytesPtr, bytes, 0, len);
        return bytes;
    }

    private static byte[]? ConvertTiffToPng(IntPtr nsTiffData)
    {
        try
        {
            // [NSBitmapImageRep imageRepWithData:nsTiffData]
            var repCls = MacNativeMethods.objc_getClass("NSBitmapImageRep");
            if (repCls == IntPtr.Zero) return null;

            var repWithDataSel = MacNativeMethods.sel_registerName("imageRepWithData:");
            var rep = MacNativeMethods.objc_msgSend(repCls, repWithDataSel, nsTiffData);
            if (rep == IntPtr.Zero) return null;

            // [rep representationUsingType:NSPNGFileType (4) properties:nil]
            var repUsingTypeSel = MacNativeMethods.sel_registerName("representationUsingType:properties:");
            var nsPngData = MacNativeMethods.objc_msgSend(rep, repUsingTypeSel, (IntPtr)4, IntPtr.Zero);
            return ReadDataBytes(nsPngData);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
