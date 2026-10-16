using System;

namespace SlimClip.Core.Services;

public static class WindowAnchorCalculator
{
    public static (int X, int Y) Calculate(
        (int X, int Y)? anchor,
        int windowWidth,
        int windowHeight,
        int workAreaLeft,
        int workAreaTop,
        int workAreaRight,
        int workAreaBottom)
    {
        int workWidth = workAreaRight - workAreaLeft;
        int workHeight = workAreaBottom - workAreaTop;

        if (!anchor.HasValue)
        {
            var centerX = workAreaLeft + (workWidth - windowWidth) / 2;
            var centerY = workAreaTop + (workHeight - windowHeight) / 2;
            return (centerX, centerY);
        }

        var (ax, ay) = anchor.Value;

        // Default offset: +16px, +24px
        int x = ax + 16;
        int y = ay + 24;

        // Horizontal flip
        if (x + windowWidth > workAreaRight - 12)
        {
            x = ax - windowWidth - 16;
        }

        // Vertical flip
        if (y + windowHeight > workAreaBottom - 12)
        {
            y = ay - windowHeight - 12;
        }

        // Clamp inside workArea
        int minX = workAreaLeft + 8;
        int maxX = Math.Max(minX, workAreaRight - windowWidth - 8);
        int minY = workAreaTop + 8;
        int maxY = Math.Max(minY, workAreaBottom - windowHeight - 8);

        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

        return (x, y);
    }
}
