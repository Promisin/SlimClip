using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Platform.MacOS;
using Xunit;

namespace SlimClip.Tests;

public class MacPasteActivationTests
{
    [Fact]
    public void MacPasteService_ActivateTargetApplication_WithInvalidObject_DoesNotCrash()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        // 构造一个合法的 NSObject 实例（如 NSView 或 NSObject），但它不支持 activateWithOptions:
        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var objCls = MacNativeMethods.objc_getClass("NSObject");
            var allocSel = MacNativeMethods.sel_registerName("alloc");
            var initSel = MacNativeMethods.sel_registerName("init");
            var dummyObj = MacNativeMethods.objc_msgSend(
                MacNativeMethods.objc_msgSend(objCls, allocSel),
                initSel);

            Assert.NotEqual(IntPtr.Zero, dummyObj);

            var pasteService = new MacPasteService();
            pasteService.TargetApplication = dummyObj;

            // 调用激活：未修复前，这一步将触发 unrecognized selector sent to instance 并导致测试进程崩溃终止！
            MacPasteService.ActivateTargetApplication(dummyObj);
        }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }

    [Fact]
    public void MacPasteService_ActivateTargetApplication_WithNonExistentPid_DoesNotCrash()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        var pasteService = new MacPasteService();
        // 传入一个不存在的虚拟 PID
        var fakePid = (IntPtr)9999999;
        pasteService.TargetApplication = fakePid;

        MacPasteService.ActivateTargetApplication(fakePid);
    }

    [Fact]
    public void MacPasteService_ActivateTargetApplication_WithZero_DoesNotCrash()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacPasteService.ActivateTargetApplication(IntPtr.Zero);
    }

    [Fact]
    public void MacWindowActivationService_GetForegroundWindowHandle_ReturnsValidPid()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        var service = new MacWindowActivationService();
        var handle = service.GetForegroundWindowHandle();

        // 返回的句柄如果非零，应当是一个正整数 PID（在正常系统范围内，通常小于 1,000,000）
        if (handle != IntPtr.Zero)
        {
            var pidValue = (long)handle;
            Assert.True(pidValue > 0, "Foreground handle must be a positive PID");
            Assert.True(pidValue < 10_000_000, "Foreground handle should be a valid process ID, not a raw heap pointer");
        }
    }

    [Fact]
    public void MacPasteService_ActivateTargetApplication_WithCurrentProcessPid_ExecutesGracefully()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        // 传入当前进程自身的 PID
        var currentPid = (IntPtr)Environment.ProcessId;
        MacPasteService.ActivateTargetApplication(currentPid);
    }

    [Fact]
    public void MacWindowActivationService_RestoreForegroundWindow_WithZero_DoesNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var service = new MacWindowActivationService();
        service.RestoreForegroundWindow(IntPtr.Zero);
    }

    [Fact]
    public void MacWindowActivationService_RestoreForegroundWindow_WithCurrentPid_ExecutesGracefully()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var service = new MacWindowActivationService();
        service.RestoreForegroundWindow((IntPtr)Environment.ProcessId);
    }

    [Fact]
    public void MacWindowActivationService_RestoreForegroundWindow_WithInvalidPid_ExecutesGracefully()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var service = new MacWindowActivationService();
        service.RestoreForegroundWindow((IntPtr)9999999);
    }
}
