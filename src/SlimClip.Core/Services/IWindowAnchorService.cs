namespace SlimClip.Core.Services;

public interface IWindowAnchorService
{
    (int X, int Y) GetActivationAnchor();
    (int X, int Y) CalculateWindowPosition(
        (int X, int Y)? anchor,
        int windowWidth,
        int windowHeight,
        int workAreaLeft,
        int workAreaTop,
        int workAreaRight,
        int workAreaBottom);
}
