using System;
using System.Globalization;

namespace SlimClip.Core.Services;

public readonly record struct ParsedColor(byte A, byte R, byte G, byte B);

public static class HexColorParser
{
    public static bool TryParse(string? text, out ParsedColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (!s.StartsWith('#'))
            return false;

        s = s[1..];
        var len = s.Length;

        // Check valid hex chars
        for (int i = 0; i < len; i++)
        {
            var c = s[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }

        try
        {
            switch (len)
            {
                case 3: // RGB -> #F00 -> R=FF, G=00, B=00, A=FF
                {
                    var r = byte.Parse(new string(s[0], 2), NumberStyles.HexNumber);
                    var g = byte.Parse(new string(s[1], 2), NumberStyles.HexNumber);
                    var b = byte.Parse(new string(s[2], 2), NumberStyles.HexNumber);
                    color = new ParsedColor(255, r, g, b);
                    return true;
                }
                case 4: // RGBA -> #F008 -> R=FF, G=00, B=00, A=88
                {
                    var r = byte.Parse(new string(s[0], 2), NumberStyles.HexNumber);
                    var g = byte.Parse(new string(s[1], 2), NumberStyles.HexNumber);
                    var b = byte.Parse(new string(s[2], 2), NumberStyles.HexNumber);
                    var a = byte.Parse(new string(s[3], 2), NumberStyles.HexNumber);
                    color = new ParsedColor(a, r, g, b);
                    return true;
                }
                case 6: // RRGGBB -> #123456
                {
                    var r = byte.Parse(s[0..2], NumberStyles.HexNumber);
                    var g = byte.Parse(s[2..4], NumberStyles.HexNumber);
                    var b = byte.Parse(s[4..6], NumberStyles.HexNumber);
                    color = new ParsedColor(255, r, g, b);
                    return true;
                }
                case 8: // Standard #AARRGGBB, e.g. #80123456 has A=80, R=12, G=34, B=56.
                {
                    var a = byte.Parse(s[0..2], NumberStyles.HexNumber);
                    var r = byte.Parse(s[2..4], NumberStyles.HexNumber);
                    var g = byte.Parse(s[4..6], NumberStyles.HexNumber);
                    var b = byte.Parse(s[6..8], NumberStyles.HexNumber);
                    color = new ParsedColor(a, r, g, b);
                    return true;
                }
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
