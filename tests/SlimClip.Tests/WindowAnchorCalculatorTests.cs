using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class WindowAnchorCalculatorTests
{
    private const int WorkLeft = 0;
    private const int WorkTop = 0;
    private const int WorkRight = 1920;
    private const int WorkBottom = 1040; // 1080 - 40 taskbar
    private const int WindowWidth = 420;
    private const int WindowHeight = 600;

    [Fact]
    public void Calculate_NormalAnchor_AppliesOffset()
    {
        var anchor = (X: 500, Y: 300);
        var pos = WindowAnchorCalculator.Calculate(
            anchor, WindowWidth, WindowHeight,
            WorkLeft, WorkTop, WorkRight, WorkBottom);

        Assert.Equal(500 + 16, pos.X);
        Assert.Equal(300 + 24, pos.Y);
    }

    [Fact]
    public void Calculate_NearRightEdge_FlipsLeftward()
    {
        // Anchor near right edge: X = 1800
        // 1800 + 16 + 420 = 2236 > 1920 - 12 (1908) -> flips!
        var anchor = (X: 1800, Y: 300);
        var pos = WindowAnchorCalculator.Calculate(
            anchor, WindowWidth, WindowHeight,
            WorkLeft, WorkTop, WorkRight, WorkBottom);

        // Flipped X: 1800 - 420 - 16 = 1364
        Assert.Equal(1364, pos.X);
        Assert.Equal(300 + 24, pos.Y);
    }

    [Fact]
    public void Calculate_NearBottomEdge_FlipsUpward()
    {
        // Anchor near bottom: Y = 900
        // 900 + 24 + 600 = 1524 > 1040 - 12 (1028) -> flips!
        var anchor = (X: 500, Y: 900);
        var pos = WindowAnchorCalculator.Calculate(
            anchor, WindowWidth, WindowHeight,
            WorkLeft, WorkTop, WorkRight, WorkBottom);

        Assert.Equal(500 + 16, pos.X);
        // Flipped Y: 900 - 600 - 12 = 288
        Assert.Equal(288, pos.Y);
    }

    [Fact]
    public void Calculate_NullAnchor_CentersInWorkArea()
    {
        var pos = WindowAnchorCalculator.Calculate(
            null, WindowWidth, WindowHeight,
            WorkLeft, WorkTop, WorkRight, WorkBottom);

        Assert.Equal((1920 - 420) / 2, pos.X);
        Assert.Equal((1040 - 600) / 2, pos.Y);
    }
}
