using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacWindowAnchorService : IWindowAnchorService
{
    public (int X, int Y) GetActivationAnchor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 1. Try Accessibility API Caret Position
            try
            {
                if (MacNativeMethods.AXIsProcessTrusted())
                {
                    var systemWide = MacNativeMethods.AXUIElementCreateSystemWide();
                    if (systemWide != IntPtr.Zero)
                    {
                        try
                        {
                            var focusedAttr = MacNativeMethods.CreateNSString("AXFocusedUIElement");
                            if (MacNativeMethods.AXUIElementCopyAttributeValue(systemWide, focusedAttr, out var focusedElement) == 0 && focusedElement != IntPtr.Zero)
                            {
                                try
                                {
                                    var posAttr = MacNativeMethods.CreateNSString("AXPosition");
                                    if (MacNativeMethods.AXUIElementCopyAttributeValue(focusedElement, posAttr, out var posVal) == 0 && posVal != IntPtr.Zero)
                                    {
                                        try
                                        {
                                            if (MacNativeMethods.AXValueGetValue(posVal, MacNativeMethods.kAXValueCGPointType, out var pt))
                                            {
                                                if (pt.X > 0 || pt.Y > 0)
                                                {
                                                    return ((int)pt.X, (int)pt.Y);
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            MacNativeMethods.CFRelease(posVal);
                                        }
                                    }
                                }
                                finally
                                {
                                    MacNativeMethods.CFRelease(focusedElement);
                                }
                            }
                        }
                        finally
                        {
                            MacNativeMethods.CFRelease(systemWide);
                        }
                    }
                }
            }
            catch { }

            // 2. Fallback to mouse cursor position via CoreGraphics
            try
            {
                var ev = MacNativeMethods.CGEventCreate(IntPtr.Zero);
                if (ev != IntPtr.Zero)
                {
                    try
                    {
                        var loc = MacNativeMethods.CGEventGetLocation(ev);
                        return ((int)loc.X, (int)loc.Y);
                    }
                    finally
                    {
                        MacNativeMethods.CFRelease(ev);
                    }
                }
            }
            catch { }
        }

        // 3. Fallback to default center
        return (700, 450);
    }

    public (int X, int Y) CalculateWindowPosition(
        (int X, int Y)? anchor,
        int windowWidth,
        int windowHeight,
        int workAreaLeft,
        int workAreaTop,
        int workAreaRight,
        int workAreaBottom)
    {
        if (workAreaRight <= workAreaLeft || workAreaBottom <= workAreaTop)
        {
            var (wLeft, wTop, wRight, wBottom) = GetMacWorkArea(anchor?.X ?? 700, anchor?.Y ?? 450);
            workAreaLeft = wLeft;
            workAreaTop = wTop;
            workAreaRight = wRight;
            workAreaBottom = wBottom;
        }

        return WindowAnchorCalculator.Calculate(
            anchor,
            windowWidth,
            windowHeight,
            workAreaLeft,
            workAreaTop,
            workAreaRight,
            workAreaBottom);
    }

    public static (int Left, int Top, int Right, int Bottom) GetMacWorkArea(int anchorX, int anchorY)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return (0, 25, 1728, 1117);
        }

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                var screenCls = MacNativeMethods.objc_getClass("NSScreen");
                var screensSel = MacNativeMethods.sel_registerName("screens");
                var screens = MacNativeMethods.objc_msgSend(screenCls, screensSel);
                if (screens == IntPtr.Zero)
                    return (0, 25, 1728, 1117);

                var countSel = MacNativeMethods.sel_registerName("count");
                var count = (int)MacNativeMethods.objc_msgSend_long(screens, countSel);
                if (count <= 0)
                    return (0, 25, 1728, 1117);

                var objectAtSel = MacNativeMethods.sel_registerName("objectAtIndex:");

                // 主屏幕用于计算 Y 轴全屏翻转基准
                var mainScreen = MacNativeMethods.objc_msgSend(screens, objectAtSel, IntPtr.Zero);
                var primaryFrame = MacNativeMethods.GetScreenFrame(mainScreen);
                double primaryHeight = primaryFrame.Height > 0 ? primaryFrame.Height : 1080;

                // 查找包含 anchor 点的显示器
                IntPtr targetScreen = mainScreen;
                MacNativeMethods.MacRect targetVisibleFrame = MacNativeMethods.GetScreenVisibleFrame(mainScreen);

                for (int i = 0; i < count; i++)
                {
                    var scr = MacNativeMethods.objc_msgSend(screens, objectAtSel, (IntPtr)i);
                    var frame = MacNativeMethods.GetScreenFrame(scr);

                    // 转换为顶部为原点的坐标进行命中测试
                    double top = primaryHeight - (frame.Y + frame.Height);
                    double bottom = primaryHeight - frame.Y;
                    double left = frame.X;
                    double right = frame.X + frame.Width;

                    if (anchorX >= left && anchorX <= right && anchorY >= top && anchorY <= bottom)
                    {
                        targetScreen = scr;
                        targetVisibleFrame = MacNativeMethods.GetScreenVisibleFrame(scr);
                        break;
                    }
                }

                // 将 targetVisibleFrame 转换为顶部为原点的 Avalonia 坐标系统
                int wLeft = (int)targetVisibleFrame.X;
                int wRight = (int)(targetVisibleFrame.X + targetVisibleFrame.Width);
                int wTop = (int)(primaryHeight - (targetVisibleFrame.Y + targetVisibleFrame.Height));
                int wBottom = (int)(primaryHeight - targetVisibleFrame.Y);

                if (wRight > wLeft && wBottom > wTop)
                {
                    return (wLeft, wTop, wRight, wBottom);
                }
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch { }

        return (0, 25, 1728, 1117);
    }
}
