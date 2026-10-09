using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacWindowActivationService : IWindowActivationService
{
    public void ActivateWindow(IntPtr windowHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || windowHandle == IntPtr.Zero)
            return;

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                var nsWindow = MacLiquidGlassHelper.ResolveNSWindow(windowHandle);
                if (nsWindow == IntPtr.Zero)
                    nsWindow = windowHandle;

                // [NSApp activateIgnoringOtherApps:YES]
                var nsAppCls = MacNativeMethods.objc_getClass("NSApplication");
                var sharedAppSel = MacNativeMethods.sel_registerName("sharedApplication");
                var app = MacNativeMethods.objc_msgSend(nsAppCls, sharedAppSel);

                var activateSel = MacNativeMethods.sel_registerName("activateIgnoringOtherApps:");
                MacNativeMethods.objc_msgSend(app, activateSel, (IntPtr)1);

                // [window makeKeyAndOrderFront:nil]
                var makeKeySel = MacNativeMethods.sel_registerName("makeKeyAndOrderFront:");
                MacNativeMethods.objc_msgSend(nsWindow, makeKeySel, IntPtr.Zero);
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch { }
    }

    public IntPtr GetForegroundWindowHandle()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IntPtr.Zero;

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                // [[NSWorkspace sharedWorkspace] frontmostApplication]
                var workspaceCls = MacNativeMethods.objc_getClass("NSWorkspace");
                var sharedWorkspaceSel = MacNativeMethods.sel_registerName("sharedWorkspace");
                var workspace = MacNativeMethods.objc_msgSend(workspaceCls, sharedWorkspaceSel);
                if (workspace == IntPtr.Zero)
                    return IntPtr.Zero;

                var frontmostSel = MacNativeMethods.sel_registerName("frontmostApplication");
                var app = MacNativeMethods.objc_msgSend(workspace, frontmostSel);
                if (app == IntPtr.Zero)
                    return IntPtr.Zero;

                // [app processIdentifier] -> pid_t
                var pidSel = MacNativeMethods.sel_registerName("processIdentifier");
                var pid = (int)MacNativeMethods.objc_msgSend(app, pidSel);
                if (pid <= 0)
                    return IntPtr.Zero;

                // 排除当前 SlimClip 自身进程，避免唤醒前就是自己时陷入自激活
                if (pid == Environment.ProcessId)
                    return IntPtr.Zero;

                return (IntPtr)pid;
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public void RestoreForegroundWindow(IntPtr windowHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || windowHandle == IntPtr.Zero)
            return;

        MacPasteService.ActivateTargetApplication(windowHandle);
    }
}
