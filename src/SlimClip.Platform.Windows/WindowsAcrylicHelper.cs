using System;
using System.Runtime.InteropServices;

namespace SlimClip.Platform.Windows;

public static class WindowsAcrylicHelper
{
    public static void ApplyDesktopAcrylic(IntPtr hwnd, bool isDark, bool isSolid = false)
    {
        if (hwnd == IntPtr.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // 1. 设置深色 / 浅色模式沉浸式响应 (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = isDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 仅在 Windows 11 (Build >= 22000) 下设置 DWM 硬件圆角裁剪与系统级背景类型；
            // Windows 10 实色模式由 UI 层通过无边框透明窗口 + 双层圆角投影渲染，Windows 10 通透模式保留亚克力并使用 0px 直角对齐。
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                // 2. 声明 Windows 11 现代圆角偏好 (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)
                int cornerPref = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

                // 3. 通透模式启用 DWM 桌面亚克力 (DWMSBT_TRANSIENTWINDOW = 3)，实色模式关闭 DWM 背景材质 (DWMSBT_NONE = 1)
                int backdropType = isSolid ? NativeMethods.DWMSBT_NONE : NativeMethods.DWMSBT_TRANSIENTWINDOW;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
        }
        catch
        {
            // 优雅降级，防止低版本操作系统抛出异常
        }
    }

    public static void UpdateThemeMode(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            int darkMode = isDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }
        catch
        {
            // 优雅降级
        }
    }

    public static void ApplyNativeMenuTheme(bool isDark)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var mode = isDark ? NativeMethods.PreferredAppMode.ForceDark : NativeMethods.PreferredAppMode.ForceLight;
            NativeMethods.SetPreferredAppMode(mode);
            NativeMethods.FlushMenuThemes();
        }
        catch
        {
            // 优雅降级，防止低版本操作系统抛出异常
        }
    }
}
