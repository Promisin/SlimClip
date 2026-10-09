using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.UI.Converters;

public class ClipboardTypeToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ClipboardType type)
        {
            return type == ClipboardType.Image;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class ImagePathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            var resolvedPath = ResolveImagePath(path);
            if (resolvedPath != null && File.Exists(resolvedPath))
            {
                try
                {
                    using var stream = File.OpenRead(resolvedPath);
                    return new Bitmap(stream);
                }
                catch
                {
                    // Fall back to null if decoding fails
                }
            }
        }

        return null;
    }

    public static string? ResolveImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // 1. Direct path (absolute or relative to current working directory)
        if (Path.IsPathRooted(path) && File.Exists(path))
            return path;

        var dataDir = SettingsService.ResolveDataDirectory();

        // 2. Relative to dataDir
        var candidate1 = Path.Combine(dataDir, path);
        if (File.Exists(candidate1)) return candidate1;

        // 3. In dataDir/images/{filename}
        var fileName = Path.GetFileName(path);
        var candidate2 = Path.Combine(dataDir, "images", fileName);
        if (File.Exists(candidate2)) return candidate2;

        // 4. Relative to AppDomain BaseDirectory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.Equals(dataDir, baseDir, StringComparison.OrdinalIgnoreCase))
        {
            var candidate3 = Path.Combine(baseDir, path);
            if (File.Exists(candidate3)) return candidate3;

            var candidate4 = Path.Combine(baseDir, "images", fileName);
            if (File.Exists(candidate4)) return candidate4;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class HexColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && HexColorParser.TryParse(text, out var color))
        {
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class HexColorVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            return HexColorParser.TryParse(text, out _);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
