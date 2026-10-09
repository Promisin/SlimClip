namespace SlimClip.Core.Services;

public static class PngHelper
{
    public static (int Width, int Height) ParsePngDimensions(byte[] png)
    {
        if (png.Length >= 24)
        {
            // PNG IHDR chunk starts at byte 12: Width at 16, Height at 20 (big-endian)
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (w, h);
        }
        return (0, 0);
    }
}
