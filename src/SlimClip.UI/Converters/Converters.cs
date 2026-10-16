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
    private const int MaxCacheSize = 100;
    private const int TargetThumbnailWidth = 360;
    private const int TargetThumbnailHeight = 220;

    private sealed class CacheEntry
    {
        public required string Path { get; init; }
        public required DateTime LastWriteTimeUtc { get; init; }
        public required Bitmap Bitmap { get; init; }
    }

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.LinkedListNode<CacheEntry>> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.LinkedList<CacheEntry> _lruList = new();
    private static readonly object _syncLock = new();

    public static void ClearCache()
    {
        lock (_syncLock)
        {
            foreach (var node in _cache.Values)
            {
                try { node.Value.Bitmap.Dispose(); } catch { }
            }
            _cache.Clear();
            _lruList.Clear();
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            var resolvedPath = ResolveImagePath(path);
            if (resolvedPath != null && File.Exists(resolvedPath))
            {
                // 1. Fast path: check LRU cache
                lock (_syncLock)
                {
                    if (_cache.TryGetValue(resolvedPath, out var node))
                    {
                        DateTime lastWrite = DateTime.MinValue;
                        try { lastWrite = File.GetLastWriteTimeUtc(resolvedPath); } catch { }

                        if (lastWrite == node.Value.LastWriteTimeUtc)
                        {
                            _lruList.Remove(node);
                            _lruList.AddFirst(node);
                            return node.Value.Bitmap;
                        }

                        // Stale entry: remove from cache
                        _lruList.Remove(node);
                        _cache.Remove(resolvedPath);
                        try { node.Value.Bitmap.Dispose(); } catch { }
                    }
                }

                // 2. Decode thumbnail
                Bitmap? bitmap = null;
                try
                {
                    bitmap = LoadThumbnail(resolvedPath);
                }
                catch
                {
                    return null;
                }

                if (bitmap == null) return null;

                // 3. Store in LRU cache
                lock (_syncLock)
                {
                    if (_cache.TryGetValue(resolvedPath, out var existingNode))
                    {
                        bitmap.Dispose();
                        _lruList.Remove(existingNode);
                        _lruList.AddFirst(existingNode);
                        return existingNode.Value.Bitmap;
                    }

                    while (_lruList.Count >= MaxCacheSize)
                    {
                        var oldest = _lruList.Last;
                        if (oldest == null) break;
                        _lruList.RemoveLast();
                        _cache.Remove(oldest.Value.Path);
                        try { oldest.Value.Bitmap.Dispose(); } catch { }
                    }

                    DateTime lastWrite = DateTime.MinValue;
                    try { lastWrite = File.GetLastWriteTimeUtc(resolvedPath); } catch { }

                    var entry = new CacheEntry
                    {
                        Path = resolvedPath,
                        LastWriteTimeUtc = lastWrite,
                        Bitmap = bitmap
                    };
                    var newNode = new System.Collections.Generic.LinkedListNode<CacheEntry>(entry);
                    _lruList.AddFirst(newNode);
                    _cache[resolvedPath] = newNode;
                    return bitmap;
                }
            }
        }

        return null;
    }

    private static Bitmap? LoadThumbnail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var dims = ImageHeaderHelper.TryReadDimensions(stream);

        if (dims.HasValue)
        {
            var (w, h) = dims.Value;
            if (w <= TargetThumbnailWidth && h <= TargetThumbnailHeight)
            {
                return new Bitmap(stream);
            }

            // Bound by height if aspect ratio is narrower/taller than target box
            if ((double)w / h < (double)TargetThumbnailWidth / TargetThumbnailHeight)
            {
                return Bitmap.DecodeToHeight(stream, TargetThumbnailHeight);
            }
            else
            {
                return Bitmap.DecodeToWidth(stream, TargetThumbnailWidth);
            }
        }

        try
        {
            return Bitmap.DecodeToWidth(stream, TargetThumbnailWidth);
        }
        catch
        {
            stream.Position = 0;
            return new Bitmap(stream);
        }
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
