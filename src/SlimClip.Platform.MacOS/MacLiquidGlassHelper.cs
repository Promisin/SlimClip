using System;
using System.Runtime.InteropServices;

namespace SlimClip.Platform.MacOS;

public static class MacLiquidGlassHelper
{
    private static bool _appKitLoaded;

    public static void EnsureAppKitLoaded()
    {
        if (_appKitLoaded || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        try
        {
            MacNativeMethods.dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 1);
            _appKitLoaded = true;
        }
        catch { }
    }

    public static bool IsLiquidGlassSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public static bool IsSystemDark()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;

        EnsureAppKitLoaded();

        try
        {
            var userDefaultsCls = MacNativeMethods.objc_getClass("NSUserDefaults");
            if (userDefaultsCls != IntPtr.Zero)
            {
                var standardSel = MacNativeMethods.sel_registerName("standardUserDefaults");
                var defaults = MacNativeMethods.objc_msgSend(userDefaultsCls, standardSel);
                if (defaults != IntPtr.Zero)
                {
                    var stringForKeySel = MacNativeMethods.sel_registerName("stringForKey:");
                    var key = MacNativeMethods.CreateNSString("AppleInterfaceStyle");
                    var val = MacNativeMethods.objc_msgSend(defaults, stringForKeySel, key);
                    var style = MacNativeMethods.ReadNSString(val);
                    if (string.Equals(style, "Dark", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public static IntPtr ResolveNSWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IntPtr.Zero;

        EnsureAppKitLoaded();

        try
        {
            var isKindSel = MacNativeMethods.sel_registerName("isKindOfClass:");

            // 1. Is it an NSWindow?
            var windowCls = MacNativeMethods.objc_getClass("NSWindow");
            if (windowCls != IntPtr.Zero && MacNativeMethods.objc_msgSend_bool(handle, isKindSel, windowCls))
            {
                return handle;
            }

            // 2. Is it an NSView? In AppKit, every NSView has a -window selector returning its NSWindow
            var viewCls = MacNativeMethods.objc_getClass("NSView");
            if (viewCls != IntPtr.Zero && MacNativeMethods.objc_msgSend_bool(handle, isKindSel, viewCls))
            {
                var windowSel = MacNativeMethods.sel_registerName("window");
                var win = MacNativeMethods.objc_msgSend(handle, windowSel);
                if (win != IntPtr.Zero)
                    return win;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    private static System.Collections.Generic.List<IntPtr> FindAllVisualEffectViews(IntPtr contentView)
    {
        var list = new System.Collections.Generic.List<IntPtr>();
        var subviewsSel = MacNativeMethods.sel_registerName("subviews");
        var subviews = MacNativeMethods.objc_msgSend(contentView, subviewsSel);
        var visualEffectCls = MacNativeMethods.objc_getClass("NSVisualEffectView");
        if (subviews != IntPtr.Zero && visualEffectCls != IntPtr.Zero)
        {
            var countSel = MacNativeMethods.sel_registerName("count");
            var count = (int)MacNativeMethods.objc_msgSend_long(subviews, countSel);
            var objectAtIndexSel = MacNativeMethods.sel_registerName("objectAtIndex:");
            var isKindOfSel = MacNativeMethods.sel_registerName("isKindOfClass:");
            for (int i = 0; i < count; i++)
            {
                var sv = MacNativeMethods.objc_msgSend(subviews, objectAtIndexSel, (IntPtr)i);
                if (MacNativeMethods.objc_msgSend_bool(sv, isKindOfSel, visualEffectCls))
                {
                    list.Add(sv);
                }
            }
        }
        return list;
    }

    private static IntPtr FindGlassEffectView(IntPtr contentView)
    {
        var glassCls = MacNativeMethods.objc_getClass("NSGlassEffectView");
        if (glassCls == IntPtr.Zero || contentView == IntPtr.Zero)
            return IntPtr.Zero;

        var subviewsSel = MacNativeMethods.sel_registerName("subviews");
        var subviews = MacNativeMethods.objc_msgSend(contentView, subviewsSel);
        if (subviews != IntPtr.Zero)
        {
            var countSel = MacNativeMethods.sel_registerName("count");
            var count = (int)MacNativeMethods.objc_msgSend_long(subviews, countSel);
            var objectAtIndexSel = MacNativeMethods.sel_registerName("objectAtIndex:");
            var isKindOfSel = MacNativeMethods.sel_registerName("isKindOfClass:");
            for (int i = 0; i < count; i++)
            {
                var sv = MacNativeMethods.objc_msgSend(subviews, objectAtIndexSel, (IntPtr)i);
                if (MacNativeMethods.objc_msgSend_bool(sv, isKindOfSel, glassCls))
                {
                    return sv;
                }
            }
        }
        return IntPtr.Zero;
    }

    public static void ApplyLiquidGlass(IntPtr handle, bool? isDark = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || handle == IntPtr.Zero)
            return;

        EnsureAppKitLoaded();

        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var nsWindow = ResolveNSWindow(handle);
            if (nsWindow == IntPtr.Zero)
                return;

            bool dark = isDark ?? IsSystemDark();

            // 1. Make window transparent so the backdrop blur shines through
            var setOpaqueSel = MacNativeMethods.sel_registerName("setOpaque:");
            MacNativeMethods.objc_msgSend_void_bool(nsWindow, setOpaqueSel, false);

            var colorCls = MacNativeMethods.objc_getClass("NSColor");
            if (colorCls != IntPtr.Zero)
            {
                var clearColorSel = MacNativeMethods.sel_registerName("clearColor");
                var clearColor = MacNativeMethods.objc_msgSend(colorCls, clearColorSel);
                if (clearColor != IntPtr.Zero)
                {
                    var setBgColorSel = MacNativeMethods.sel_registerName("setBackgroundColor:");
                    MacNativeMethods.objc_msgSend_void(nsWindow, setBgColorSel, clearColor);
                }
            }

            // Obtain Appearance
            var appearanceCls = MacNativeMethods.objc_getClass("NSAppearance");
            var appearanceNamedSel = MacNativeMethods.sel_registerName("appearanceNamed:");
            var appearanceName = dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
            var nsAppearanceName = MacNativeMethods.CreateNSString(appearanceName);
            var appearance = appearanceCls != IntPtr.Zero
                ? MacNativeMethods.objc_msgSend(appearanceCls, appearanceNamedSel, nsAppearanceName)
                : IntPtr.Zero;

            var setAppearanceSel = MacNativeMethods.sel_registerName("setAppearance:");
            if (appearance != IntPtr.Zero)
            {
                MacNativeMethods.objc_msgSend_void(nsWindow, setAppearanceSel, appearance);
            }

            // 2. Get window's contentView
            var contentViewSel = MacNativeMethods.sel_registerName("contentView");
            var contentView = MacNativeMethods.objc_msgSend(nsWindow, contentViewSel);
            if (contentView == IntPtr.Zero)
                return;

            if (appearance != IntPtr.Zero)
            {
                MacNativeMethods.objc_msgSend_void(contentView, setAppearanceSel, appearance);
            }

            // 3. Get contentView bounds so effectView covers the exact window surface
            var boundsSel = MacNativeMethods.sel_registerName("bounds");
            var bounds = MacNativeMethods.objc_msgSend_CGRect(contentView, boundsSel);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = new MacNativeMethods.CGRect(0, 0, 560, 540);
            }

            var glassCls = MacNativeMethods.objc_getClass("NSGlassEffectView");
            var setFrameSel = MacNativeMethods.sel_registerName("setFrame:");
            var setHiddenSel = MacNativeMethods.sel_registerName("setHidden:");
            var setNeedsDisplaySel = MacNativeMethods.sel_registerName("setNeedsDisplay:");
            var setCornerRadiusSel = MacNativeMethods.sel_registerName("setCornerRadius:");

            if (glassCls != IntPtr.Zero)
            {
                // Native macOS NSGlassEffectView (Apple Liquid Glass)
                // Hide any Avalonia built-in NSVisualEffectView that defaults to milky light blur
                var effectViews = FindAllVisualEffectViews(contentView);
                foreach (var ev in effectViews)
                {
                    MacNativeMethods.objc_msgSend_void_bool(ev, setHiddenSel, true);
                }

                var existingGlass = FindGlassEffectView(contentView);
                if (existingGlass == IntPtr.Zero)
                {
                    var allocSel = MacNativeMethods.sel_registerName("alloc");
                    var initSel = MacNativeMethods.sel_registerName("init");
                    var glassView = MacNativeMethods.objc_msgSend(MacNativeMethods.objc_msgSend(glassCls, allocSel), initSel);
                    if (glassView != IntPtr.Zero)
                    {
                        MacNativeMethods.objc_msgSend_void_CGRect(glassView, setFrameSel, bounds);
                        var setAutoresizingMaskSel = MacNativeMethods.sel_registerName("setAutoresizingMask:");
                        MacNativeMethods.objc_msgSend(glassView, setAutoresizingMaskSel, (IntPtr)18); // WidthSizable (2) | HeightSizable (16) = 18

                        if (appearance != IntPtr.Zero)
                        {
                            MacNativeMethods.objc_msgSend_void(glassView, setAppearanceSel, appearance);
                        }

                        MacNativeMethods.objc_msgSend_void_double(glassView, setCornerRadiusSel, 14.0);

                        var addSubviewSel = MacNativeMethods.sel_registerName("addSubview:positioned:relativeTo:");
                        MacNativeMethods.objc_msgSend(contentView, addSubviewSel, glassView, (IntPtr)(-1), IntPtr.Zero);
                    }
                }
                else
                {
                    MacNativeMethods.objc_msgSend_void_bool(existingGlass, setHiddenSel, false);
                    MacNativeMethods.objc_msgSend_void_CGRect(existingGlass, setFrameSel, bounds);
                    if (appearance != IntPtr.Zero)
                    {
                        MacNativeMethods.objc_msgSend_void(existingGlass, setAppearanceSel, appearance);
                    }
                    MacNativeMethods.objc_msgSend_void_double(existingGlass, setCornerRadiusSel, 14.0);
                    MacNativeMethods.objc_msgSend_void_bool(existingGlass, setNeedsDisplaySel, true);
                }
            }
            else
            {
                // Fallback for older macOS: NSVisualEffectView with HUDWindow(13) / Popover(6)
                var effectViews = FindAllVisualEffectViews(contentView);
                var setMaterialSel = MacNativeMethods.sel_registerName("setMaterial:");
                var setBlendingModeSel = MacNativeMethods.sel_registerName("setBlendingMode:");
                var setStateSel = MacNativeMethods.sel_registerName("setState:");
                var setWantsLayerSel = MacNativeMethods.sel_registerName("setWantsLayer:");
                var layerSel = MacNativeMethods.sel_registerName("layer");
                var setMasksToBoundsSel = MacNativeMethods.sel_registerName("setMasksToBounds:");
                var fallbackMaterial = dark ? 13 : 6; // HUDWindow for dark obsidian, Popover for light

                if (effectViews.Count > 0)
                {
                    foreach (var ev in effectViews)
                    {
                        MacNativeMethods.objc_msgSend_void_bool(ev, setHiddenSel, false);
                        MacNativeMethods.objc_msgSend_void_CGRect(ev, setFrameSel, bounds);

                        if (appearance != IntPtr.Zero)
                        {
                            MacNativeMethods.objc_msgSend_void(ev, setAppearanceSel, appearance);
                        }

                        MacNativeMethods.objc_msgSend(ev, setMaterialSel, (IntPtr)fallbackMaterial);
                        MacNativeMethods.objc_msgSend(ev, setBlendingModeSel, (IntPtr)0); // BehindWindow
                        MacNativeMethods.objc_msgSend(ev, setStateSel, (IntPtr)1); // Active

                        MacNativeMethods.objc_msgSend_void_bool(ev, setWantsLayerSel, true);
                        var evLayer = MacNativeMethods.objc_msgSend(ev, layerSel);
                        if (evLayer != IntPtr.Zero)
                        {
                            MacNativeMethods.objc_msgSend_void_double(evLayer, setCornerRadiusSel, 14.0);
                            MacNativeMethods.objc_msgSend_void_bool(evLayer, setMasksToBoundsSel, true);
                        }

                        MacNativeMethods.objc_msgSend_void_bool(ev, setNeedsDisplaySel, true);
                    }
                }
                else
                {
                    var visualEffectCls = MacNativeMethods.objc_getClass("NSVisualEffectView");
                    if (visualEffectCls != IntPtr.Zero)
                    {
                        var allocSel = MacNativeMethods.sel_registerName("alloc");
                        var initSel = MacNativeMethods.sel_registerName("init");
                        var effectView = MacNativeMethods.objc_msgSend(MacNativeMethods.objc_msgSend(visualEffectCls, allocSel), initSel);
                        if (effectView != IntPtr.Zero)
                        {
                            MacNativeMethods.objc_msgSend_void_CGRect(effectView, setFrameSel, bounds);

                            var setAutoresizingMaskSel = MacNativeMethods.sel_registerName("setAutoresizingMask:");
                            MacNativeMethods.objc_msgSend(effectView, setAutoresizingMaskSel, (IntPtr)18);

                            if (appearance != IntPtr.Zero)
                            {
                                MacNativeMethods.objc_msgSend_void(effectView, setAppearanceSel, appearance);
                            }

                            MacNativeMethods.objc_msgSend(effectView, setMaterialSel, (IntPtr)fallbackMaterial);
                            MacNativeMethods.objc_msgSend(effectView, setBlendingModeSel, (IntPtr)0);
                            MacNativeMethods.objc_msgSend(effectView, setStateSel, (IntPtr)1);

                            MacNativeMethods.objc_msgSend_void_bool(effectView, setWantsLayerSel, true);
                            var layer = MacNativeMethods.objc_msgSend(effectView, layerSel);
                            if (layer != IntPtr.Zero)
                            {
                                MacNativeMethods.objc_msgSend_void_double(layer, setCornerRadiusSel, 14.0);
                                MacNativeMethods.objc_msgSend_void_bool(layer, setMasksToBoundsSel, true);
                            }

                            var addSubviewSel = MacNativeMethods.sel_registerName("addSubview:positioned:relativeTo:");
                            MacNativeMethods.objc_msgSend(contentView, addSubviewSel, effectView, (IntPtr)(-1), IntPtr.Zero);
                        }
                    }
                }
            }

            // Native window drop shadow
            var setHasShadowSel = MacNativeMethods.sel_registerName("setHasShadow:");
            MacNativeMethods.objc_msgSend_void_bool(nsWindow, setHasShadowSel, true);
            var invalidateShadowSel = MacNativeMethods.sel_registerName("invalidateShadow");
            MacNativeMethods.objc_msgSend_void_noargs(nsWindow, invalidateShadowSel);
        }
        catch { }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }

    public static void UpdateThemeMode(IntPtr handle, bool? isDark)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || handle == IntPtr.Zero)
            return;

        EnsureAppKitLoaded();

        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            var nsWindow = ResolveNSWindow(handle);
            if (nsWindow == IntPtr.Zero)
                return;

            bool dark = isDark ?? IsSystemDark();

            var appearanceCls = MacNativeMethods.objc_getClass("NSAppearance");
            if (appearanceCls == IntPtr.Zero) return;

            var appearanceNamedSel = MacNativeMethods.sel_registerName("appearanceNamed:");
            var appearanceName = dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
            var nsAppearanceName = MacNativeMethods.CreateNSString(appearanceName);
            var appearance = MacNativeMethods.objc_msgSend(appearanceCls, appearanceNamedSel, nsAppearanceName);
            if (appearance == IntPtr.Zero) return;

            var setAppearanceSel = MacNativeMethods.sel_registerName("setAppearance:");

            // 1. Update NSWindow appearance
            MacNativeMethods.objc_msgSend_void(nsWindow, setAppearanceSel, appearance);

            // 2. Update contentView appearance
            var contentViewSel = MacNativeMethods.sel_registerName("contentView");
            var contentView = MacNativeMethods.objc_msgSend(nsWindow, contentViewSel);
            if (contentView != IntPtr.Zero)
            {
                MacNativeMethods.objc_msgSend_void(contentView, setAppearanceSel, appearance);

                // Update NSGlassEffectView
                var glassView = FindGlassEffectView(contentView);
                var setNeedsDisplaySel = MacNativeMethods.sel_registerName("setNeedsDisplay:");
                if (glassView != IntPtr.Zero)
                {
                    MacNativeMethods.objc_msgSend_void(glassView, setAppearanceSel, appearance);
                    MacNativeMethods.objc_msgSend_void_bool(glassView, setNeedsDisplaySel, true);
                }

                // Update any NSVisualEffectViews
                var effectViews = FindAllVisualEffectViews(contentView);
                var setMaterialSel = MacNativeMethods.sel_registerName("setMaterial:");
                var fallbackMaterial = dark ? 13 : 6;
                foreach (var ev in effectViews)
                {
                    MacNativeMethods.objc_msgSend_void(ev, setAppearanceSel, appearance);
                    if (glassView == IntPtr.Zero)
                    {
                        MacNativeMethods.objc_msgSend(ev, setMaterialSel, (IntPtr)fallbackMaterial);
                    }
                    MacNativeMethods.objc_msgSend_void_bool(ev, setNeedsDisplaySel, true);
                }

                var setNeedsDisplayContentSel = MacNativeMethods.sel_registerName("setNeedsDisplay:");
                MacNativeMethods.objc_msgSend_void_bool(contentView, setNeedsDisplayContentSel, true);
            }

            var invalidateShadowSel = MacNativeMethods.sel_registerName("invalidateShadow");
            MacNativeMethods.objc_msgSend_void_noargs(nsWindow, invalidateShadowSel);
        }
        catch { }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }

    public static void UpdateAppAppearance(bool? isDark)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        EnsureAppKitLoaded();

        var pool = MacNativeMethods.objc_autoreleasePoolPush();
        try
        {
            bool dark = isDark ?? IsSystemDark();

            var appearanceCls = MacNativeMethods.objc_getClass("NSAppearance");
            if (appearanceCls == IntPtr.Zero) return;

            var appearanceNamedSel = MacNativeMethods.sel_registerName("appearanceNamed:");
            var appearanceName = dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
            var nsAppearanceName = MacNativeMethods.CreateNSString(appearanceName);
            var appearance = MacNativeMethods.objc_msgSend(appearanceCls, appearanceNamedSel, nsAppearanceName);
            if (appearance == IntPtr.Zero) return;

            var appCls = MacNativeMethods.objc_getClass("NSApplication");
            var sharedSel = MacNativeMethods.sel_registerName("sharedApplication");
            var app = MacNativeMethods.objc_msgSend(appCls, sharedSel);
            if (app != IntPtr.Zero)
            {
                var setAppearanceSel = MacNativeMethods.sel_registerName("setAppearance:");
                MacNativeMethods.objc_msgSend_void(app, setAppearanceSel, appearance);
            }
        }
        catch { }
        finally
        {
            MacNativeMethods.objc_autoreleasePoolPop(pool);
        }
    }

    public static void ConfigureAccessoryApp()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        EnsureAppKitLoaded();

        try
        {
            // NSApplication.sharedApplication.setActivationPolicy(NSApplicationActivationPolicyAccessory: 1)
            var appCls = MacNativeMethods.objc_getClass("NSApplication");
            var sharedSel = MacNativeMethods.sel_registerName("sharedApplication");
            var app = MacNativeMethods.objc_msgSend(appCls, sharedSel);

            var setPolicySel = MacNativeMethods.sel_registerName("setActivationPolicy:");
            MacNativeMethods.objc_msgSend(app, setPolicySel, (IntPtr)1);

            // Sync initial system appearance
            UpdateAppAppearance(null);
        }
        catch { }
    }
}
