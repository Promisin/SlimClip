using System;
using System.Runtime.InteropServices;

namespace SlimClip.Platform.Windows;

public static class WindowsAcrylicHelper
{
    public static void ApplyDesktopAcrylic(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // 1. 设置深色 / 浅色模式沉浸式响应 (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = isDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 2. 声明 Windows 11 现代圆角偏好 (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)
            int cornerPref = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            // 3. 启用 Windows 11 纯净高级桌面亚克力 (DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_TRANSIENTWINDOW = 3)
            int backdropType = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
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
