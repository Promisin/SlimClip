using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacPasteService : IPasteService
{
    public IntPtr TargetApplication { get; set; }

    public async Task PasteAsync(ClipboardItem item, int delayMs)
    {
        await SetClipboardDataAsync(item);

        if (TargetApplication != IntPtr.Zero)
        {
            ActivateTargetApplication(TargetApplication);
            TargetApplication = IntPtr.Zero;
        }

        // 访达或终端等宿主在窗口切换交接时通常需要稍充裕的调度时间
        int effectiveDelay = (item.IsFile || item.IsImage) ? Math.Max(delayMs, 100) : delayMs;
        if (effectiveDelay > 0)
        {
            await Task.Delay(effectiveDelay);
        }

        SimulateCmdV();
    }

    public Task SetClipboardDataAsync(ClipboardItem item)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Task.CompletedTask;

        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var pboardCls = MacNativeMethods.objc_getClass("NSPasteboard");
            var genSel = MacNativeMethods.sel_registerName("generalPasteboard");
            var pboard = MacNativeMethods.objc_msgSend(pboardCls, genSel);

            var clearSel = MacNativeMethods.sel_registerName("clearContents");
            MacNativeMethods.objc_msgSend(pboard, clearSel);

            if (item.IsText)
            {
                var strType = MacNativeMethods.CreateNSString("public.utf8-plain-text");
                var nsText = MacNativeMethods.CreateNSString(item.Text ?? string.Empty);
                var setStrSel = MacNativeMethods.sel_registerName("setString:forType:");
                MacNativeMethods.objc_msgSend_bool(pboard, setStrSel, nsText, strType);

                if (!string.IsNullOrEmpty(item.HtmlContent))
                {
                    var htmlType = MacNativeMethods.CreateNSString("public.html");
                    var nsHtml = MacNativeMethods.CreateNSString(item.HtmlContent);
                    MacNativeMethods.objc_msgSend_bool(pboard, setStrSel, nsHtml, htmlType);
                }

                if (!string.IsNullOrEmpty(item.RtfContent))
                {
                    var rtfType = MacNativeMethods.CreateNSString("public.rtf");
                    var nsRtf = MacNativeMethods.CreateNSString(item.RtfContent);
                    MacNativeMethods.objc_msgSend_bool(pboard, setStrSel, nsRtf, rtfType);
                }
            }
            else if (item.IsImage && !string.IsNullOrEmpty(item.ImagePath))
            {
                var dataDir = SettingsService.ResolveDataDirectory();
                var fullPath = Path.IsPathRooted(item.ImagePath) ? item.ImagePath : Path.Combine(dataDir, item.ImagePath);
                if (File.Exists(fullPath))
                {
                    var bytes = File.ReadAllBytes(fullPath);
                    var dataCls = MacNativeMethods.objc_getClass("NSData");
                    var dataWithBytesSel = MacNativeMethods.sel_registerName("dataWithBytes:length:");

                    var ptr = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, ptr, bytes.Length);
                        var nsData = MacNativeMethods.objc_msgSend(dataCls, dataWithBytesSel, ptr, (IntPtr)bytes.Length);
                        var pngType = MacNativeMethods.CreateNSString("public.png");
                        var setDataSel = MacNativeMethods.sel_registerName("setData:forType:");
                        MacNativeMethods.objc_msgSend_bool(pboard, setDataSel, nsData, pngType);

                        // 导出标准 PNG 文件并写入 NSFilenamesPboardType 与 public.file-url，支持在访达中直接粘贴为文件
                        var exportPath = PrepareExportImageFile(dataDir, item, bytes);
                        if (!string.IsNullOrEmpty(exportPath) && File.Exists(exportPath))
                        {
                            WriteFileDropToPasteboard(pboard, new[] { exportPath }, setPlainText: false);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            }
            else if (item.IsFile && item.ParsedFilePaths.Length > 0)
            {
                WriteFileDropToPasteboard(pboard, item.ParsedFilePaths, setPlainText: true);
            }
        }
        catch { }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }

        return Task.CompletedTask;
    }

    private static void WriteFileDropToPasteboard(IntPtr pboard, string[] files, bool setPlainText)
    {
        var arrayCls = MacNativeMethods.objc_getClass("NSMutableArray");
        var arraySel = MacNativeMethods.sel_registerName("array");
        var nsArray = MacNativeMethods.objc_msgSend(arrayCls, arraySel);
        var addObjectSel = MacNativeMethods.sel_registerName("addObject:");

        foreach (var file in files)
        {
            var nsPath = MacNativeMethods.CreateNSString(file);
            MacNativeMethods.objc_msgSend(nsArray, addObjectSel, nsPath);
        }

        var filenamesType = MacNativeMethods.CreateNSString("NSFilenamesPboardType");
        var setPlistSel = MacNativeMethods.sel_registerName("setPropertyList:forType:");
        MacNativeMethods.objc_msgSend_bool(pboard, setPlistSel, nsArray, filenamesType);

        var fileUrlType = MacNativeMethods.CreateNSString("public.file-url");
        var nsFirstUrl = MacNativeMethods.CreateNSString("file://" + files[0]);
        var setStrSel = MacNativeMethods.sel_registerName("setString:forType:");
        MacNativeMethods.objc_msgSend_bool(pboard, setStrSel, nsFirstUrl, fileUrlType);

        if (setPlainText)
        {
            // 同时提供纯文本格式，允许在终端、VS Code 及纯文本输入框中粘贴出文件路径
            var strType = MacNativeMethods.CreateNSString("public.utf8-plain-text");
            var allPathsText = string.Join("\n", files);
            var nsAllPaths = MacNativeMethods.CreateNSString(allPathsText);
            MacNativeMethods.objc_msgSend_bool(pboard, setStrSel, nsAllPaths, strType);
        }
    }

    private static string? PrepareExportImageFile(string dataDir, ClipboardItem item, byte[] bytes)
    {
        try
        {
            var exportDir = Path.Combine(dataDir, "export");
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            var shortHash = !string.IsNullOrEmpty(item.ImageHash) && item.ImageHash.Length >= 4
                ? "_" + item.ImageHash[..4]
                : string.Empty;
            var fileName = $"Image_{item.CopiedAt:yyyyMMdd_HHmmss}{shortHash}.png";
            var exportPath = Path.Combine(exportDir, fileName);

            File.WriteAllBytes(exportPath, bytes);
            return exportPath;
        }
        catch
        {
            return null;
        }
    }

    internal static void ActivateTargetApplication(IntPtr targetApp)
    {
        if (targetApp == IntPtr.Zero)
            return;

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                IntPtr appInstance = IntPtr.Zero;
                long val = (long)targetApp;

                // 如果 val 在合理的 PID 范围内（1 到 10,000,000），通过 NSRunningApplication 查找实时应用对象
                if (val > 0 && val < 10_000_000)
                {
                    var runningAppCls = MacNativeMethods.objc_getClass("NSRunningApplication");
                    var appWithPidSel = MacNativeMethods.sel_registerName("runningApplicationWithProcessIdentifier:");
                    appInstance = MacNativeMethods.objc_msgSend(runningAppCls, appWithPidSel, (IntPtr)val);
                }
                else
                {
                    // 否则说明传入的是一个对象指针（例如测试用例或直接指针）
                    appInstance = targetApp;
                }

                if (appInstance == IntPtr.Zero)
                    return;

                // 防御性校验：检查该对象是否能响应 activateWithOptions:
                var respondsToSel = MacNativeMethods.sel_registerName("respondsToSelector:");
                var activateWithOptionsSel = MacNativeMethods.sel_registerName("activateWithOptions:");

                bool canActivate = MacNativeMethods.objc_msgSend_bool(appInstance, respondsToSel, activateWithOptionsSel);
                if (!canActivate)
                    return;

                // 检查 isTerminated
                var isTerminatedSel = MacNativeMethods.sel_registerName("isTerminated");
                if (MacNativeMethods.objc_msgSend_bool(appInstance, respondsToSel, isTerminatedSel))
                {
                    bool isTerminated = MacNativeMethods.objc_msgSend_bool(appInstance, isTerminatedSel);
                    if (isTerminated)
                        return;
                }

                // 确认能响应且未终止后，安全调用 [appInstance activateWithOptions:NSApplicationActivateIgnoringOtherApps (1 << 1)]
                MacNativeMethods.objc_msgSend_bool(appInstance, activateWithOptionsSel, (IntPtr)(1 << 1));
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch
        {
            // 防御未预期的托管异常
        }
    }

    private static void SimulateCmdV()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        try
        {
            // Create KeyDown for 'V' with Command flag
            var eventDown = MacNativeMethods.CGEventCreateKeyboardEvent(IntPtr.Zero, MacNativeMethods.kVK_ANSI_V, true);
            MacNativeMethods.CGEventSetFlags(eventDown, MacNativeMethods.kCGEventFlagMaskCommand);
            MacNativeMethods.CGEventPost(MacNativeMethods.kCGHIDEventTap, eventDown);
            MacNativeMethods.CFRelease(eventDown);

            // Create KeyUp for 'V'
            var eventUp = MacNativeMethods.CGEventCreateKeyboardEvent(IntPtr.Zero, MacNativeMethods.kVK_ANSI_V, false);
            MacNativeMethods.CGEventSetFlags(eventUp, 0);
            MacNativeMethods.CGEventPost(MacNativeMethods.kCGHIDEventTap, eventUp);
            MacNativeMethods.CFRelease(eventUp);
        }
        catch { }
    }
}
