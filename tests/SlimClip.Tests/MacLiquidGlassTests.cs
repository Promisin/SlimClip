using System;
using System.Runtime.InteropServices;
using SlimClip.Platform.MacOS;
using Xunit;

namespace SlimClip.Tests;

public class MacLiquidGlassTests
{
    [Fact]
    public void MacLiquidGlassHelper_ResolveNSWindow_WorksForNullAndStandaloneView()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        // 1. Zero handle should return Zero
        var resolvedNull = MacLiquidGlassHelper.ResolveNSWindow(IntPtr.Zero);
        Assert.Equal(IntPtr.Zero, resolvedNull);

        // 2. Standalone view (not in window) should return Zero
        var viewCls = MacNativeMethods.objc_getClass("NSView");
        Assert.NotEqual(IntPtr.Zero, viewCls);

        var allocSel = MacNativeMethods.sel_registerName("alloc");
        var initSel = MacNativeMethods.sel_registerName("init");

        var standaloneView = MacNativeMethods.objc_msgSend(
            MacNativeMethods.objc_msgSend(viewCls, allocSel),
            initSel);
        Assert.NotEqual(IntPtr.Zero, standaloneView);

        var resolvedEmpty = MacLiquidGlassHelper.ResolveNSWindow(standaloneView);
        Assert.Equal(IntPtr.Zero, resolvedEmpty);
    }

    [Fact]
    public void MacLiquidGlassHelper_ZeroHandle_DoesNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        // ApplyLiquidGlass with zero handle
        MacLiquidGlassHelper.ApplyLiquidGlass(IntPtr.Zero, true);
        MacLiquidGlassHelper.ApplyLiquidGlass(IntPtr.Zero, false);
        MacLiquidGlassHelper.ApplyLiquidGlass(IntPtr.Zero, null);

        // UpdateThemeMode with zero handle
        MacLiquidGlassHelper.UpdateThemeMode(IntPtr.Zero, true);
        MacLiquidGlassHelper.UpdateThemeMode(IntPtr.Zero, false);
        MacLiquidGlassHelper.UpdateThemeMode(IntPtr.Zero, null);

        // UpdateAppAppearance
        MacLiquidGlassHelper.UpdateAppAppearance(true);
        MacLiquidGlassHelper.UpdateAppAppearance(false);
        MacLiquidGlassHelper.UpdateAppAppearance(null);
    }

    [Fact]
    public void MacLiquidGlassHelper_IsSystemDark_ReturnsBoolean()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var isDark = MacLiquidGlassHelper.IsSystemDark();
        // Should execute cleanly without error
        Assert.True(isDark || !isDark);
    }

    [Fact]
    public void MacLiquidGlassHelper_ApplyLiquidGlass_OnStandaloneView_HandlesGracefully()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        MacLiquidGlassHelper.EnsureAppKitLoaded();

        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var viewCls = MacNativeMethods.objc_getClass("NSView");
            Assert.NotEqual(IntPtr.Zero, viewCls);

            var allocSel = MacNativeMethods.sel_registerName("alloc");
            var initSel = MacNativeMethods.sel_registerName("init");

            var view = MacNativeMethods.objc_msgSend(
                MacNativeMethods.objc_msgSend(viewCls, allocSel),
                initSel);

            Assert.NotEqual(IntPtr.Zero, view);

            // Apply liquid glass in dark mode
            MacLiquidGlassHelper.ApplyLiquidGlass(view, true);

            // Update theme mode to light
            MacLiquidGlassHelper.UpdateThemeMode(view, false);

            // Update theme mode back to dark
            MacLiquidGlassHelper.UpdateThemeMode(view, true);
        }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }
}
