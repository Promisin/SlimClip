using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using SlimClip.Core.Models;

namespace SlimClip.UI.Helpers;

public readonly record struct WindowChromeMetrics(
    WindowDecorations Decorations,
    IReadOnlyList<WindowTransparencyLevel> TransparencyLevels,
    CornerRadius WindowCornerRadius,
    Thickness ShadowMargin,
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    double MaxWidth,
    double MaxHeight);

public static class WindowsPlatformChromeHelper
{
    public const double Win10ShadowMargin = 14.0;

    private static readonly IReadOnlyList<WindowTransparencyLevel> TransparentOnlyLevels =
        new[] { WindowTransparencyLevel.Transparent };

    private static readonly IReadOnlyList<WindowTransparencyLevel> AcrylicTransparencyLevels =
        new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent };

    private static readonly Dictionary<Window, (double W, double H, double MinW, double MinH, double MaxW, double MaxH)> BaseBoundsCache = new();

    /// <summary>
    /// 检测当前操作系统是否为不支持 DWM 硬件圆角裁剪的 Windows 10（Build &lt; 22000）。
    /// </summary>
    public static bool IsWindows10OrOlder()
    {
        return IsWindows10Build(OperatingSystem.IsWindows(), Environment.OSVersion.Version);
    }

    /// <summary>
    /// 纯函数：根据平台与内核版本判定是否为 Windows 10（Build &lt; 22000）。
    /// </summary>
    public static bool IsWindows10Build(bool isWindows, Version osVersion)
    {
        if (!isWindows)
            return false;

        if (osVersion.Major < 10)
            return true;

        return osVersion.Major == 10 && osVersion.Build < 22000;
    }

    /// <summary>
    /// 纯函数：根据当前操作系统（Win10 / Win11 / macOS）与用户选择的主题风格（通透 Translucent / 实色 Solid）计算窗口几何与透明策略。
    /// 规则：
    /// 1. 在 Windows 10 上，若选择「通透」，保留原生亚克力并采用 0px 直角边框，彻底消除圆角与矩形亚克力错位尖角；
    /// 2. 在 Windows 10 上，若选择「实色」，采用 8px 圆角 + 无边框透明底 + 14px 双层自绘圆角阴影；
    /// 3. 其它系统与版本（Windows 11 / macOS）的任何主题风格均保持原生硬件圆角（Win11: 8px, macOS: 14px）。
    /// </summary>
    public static WindowChromeMetrics CalculateChromeMetrics(
        bool isWindows,
        bool isWin10,
        AppBackdropStyle backdropStyle,
        double baseWidth,
        double baseHeight,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight)
    {
        double defaultRadius = isWindows ? 8.0 : 14.0;

        // 情况 1：Windows 10 + 通透（保留亚克力，不要圆角）
        if (isWin10 && backdropStyle == AppBackdropStyle.Translucent)
        {
            return new WindowChromeMetrics(
                WindowDecorations.BorderOnly,
                AcrylicTransparencyLevels,
                new CornerRadius(0),
                new Thickness(0),
                baseWidth,
                baseHeight,
                minWidth,
                minHeight,
                maxWidth,
                maxHeight);
        }

        // 情况 2：Windows 10 + 实色（保留 8px 圆角，无边框透明 + 14px 自绘圆角阴影边距）
        if (isWin10 && backdropStyle == AppBackdropStyle.Solid)
        {
            double pad = Win10ShadowMargin * 2.0;
            return new WindowChromeMetrics(
                WindowDecorations.None,
                TransparentOnlyLevels,
                new CornerRadius(8),
                new Thickness(Win10ShadowMargin),
                AddPaddingIfFinite(baseWidth, pad),
                AddPaddingIfFinite(baseHeight, pad),
                AddPaddingIfFinite(minWidth, pad),
                AddPaddingIfFinite(minHeight, pad),
                AddPaddingIfFinite(maxWidth, pad),
                AddPaddingIfFinite(maxHeight, pad));
        }

        // 情况 3：Windows 11 或 macOS + 实色（保留硬件圆角与系统圆角阴影，关闭透明模糊穿透）
        if (backdropStyle == AppBackdropStyle.Solid)
        {
            return new WindowChromeMetrics(
                WindowDecorations.BorderOnly,
                TransparentOnlyLevels,
                new CornerRadius(defaultRadius),
                new Thickness(0),
                baseWidth,
                baseHeight,
                minWidth,
                minHeight,
                maxWidth,
                maxHeight);
        }

        // 情况 4：Windows 11 或 macOS + 通透（原生亚克力 / 液态玻璃 + 硬件圆角）
        return new WindowChromeMetrics(
            WindowDecorations.BorderOnly,
            AcrylicTransparencyLevels,
            new CornerRadius(defaultRadius),
            new Thickness(0),
            baseWidth,
            baseHeight,
            minWidth,
            minHeight,
            maxWidth,
            maxHeight);
    }

    /// <summary>
    /// 兼容旧签名（默认按 Windows 10 实色圆角或 Win11 通透计算）。
    /// </summary>
    public static WindowChromeMetrics CalculateChromeMetrics(
        bool isWin10,
        double baseWidth,
        double baseHeight,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight)
    {
        return CalculateChromeMetrics(
            isWindows: true,
            isWin10: isWin10,
            backdropStyle: isWin10 ? AppBackdropStyle.Solid : AppBackdropStyle.Translucent,
            baseWidth,
            baseHeight,
            minWidth,
            minHeight,
            maxWidth,
            maxHeight);
    }

    /// <summary>
    /// 根据当前主题风格动态配置窗口的边框装饰、透明层级与补偿边距。
    /// </summary>
    public static void ConfigureWindowChrome(Window window, AppBackdropStyle backdropStyle = AppBackdropStyle.Translucent)
    {
        if (!BaseBoundsCache.TryGetValue(window, out var baseBounds))
        {
            baseBounds = (window.Width, window.Height, window.MinWidth, window.MinHeight, window.MaxWidth, window.MaxHeight);
            BaseBoundsCache[window] = baseBounds;
            window.Closed += (_, _) => BaseBoundsCache.Remove(window);
        }

        bool isWin = OperatingSystem.IsWindows();
        bool isWin10 = IsWindows10OrOlder();

        var metrics = CalculateChromeMetrics(
            isWin,
            isWin10,
            backdropStyle,
            baseBounds.W,
            baseBounds.H,
            baseBounds.MinW,
            baseBounds.MinH,
            baseBounds.MaxW,
            baseBounds.MaxH);

        window.WindowDecorations = metrics.Decorations;
        window.TransparencyLevelHint = metrics.TransparencyLevels;
        window.MinWidth = metrics.MinWidth;
        window.MinHeight = metrics.MinHeight;
        window.MaxWidth = metrics.MaxWidth;
        window.MaxHeight = metrics.MaxHeight;
        window.Width = metrics.Width;
        window.Height = metrics.Height;
    }

    private static double AddPaddingIfFinite(double value, double pad)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return value;
        return value + pad;
    }
}
