using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SlimClip.Core.Models;

public partial class ClipboardItem : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string? _htmlContent;

    [ObservableProperty]
    private string? _rtfContent;

    [ObservableProperty]
    private ClipboardType _type = ClipboardType.Text;

    [ObservableProperty]
    private DateTime _copiedAt = DateTime.Now;

    [ObservableProperty]
    private bool _isFavorite;

    public bool IsPinned
    {
        get => IsFavorite;
        set => IsFavorite = value;
    }

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private int _imageWidth;

    [ObservableProperty]
    private int _imageHeight;

    [ObservableProperty]
    private string? _imageHash;

    [ObservableProperty]
    private string? _filePaths;

    public bool IsText => Type == ClipboardType.Text;
    public bool IsImage => Type == ClipboardType.Image;
    public bool IsFile => Type == ClipboardType.File;
    public static Func<DateTime, string>? DateFormatter { get; set; }

    public string CopiedAtDisplay
    {
        get
        {
            if (DateFormatter != null)
                return DateFormatter(CopiedAt);

            var now = DateTime.Now;
            if (CopiedAt.Date == now.Date)
                return $"今天 {CopiedAt:HH:mm:ss}";
            if (CopiedAt.Date == now.AddDays(-1).Date)
                return $"昨天 {CopiedAt:HH:mm:ss}";
            return CopiedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public void NotifyCopiedAtChanged()
    {
        OnPropertyChanged(nameof(CopiedAtDisplay));
    }

    public string ImageSizeDisplay => IsImage && ImageWidth > 0 && ImageHeight > 0
        ? $"{ImageWidth} × {ImageHeight}"
        : string.Empty;

    private static readonly System.Collections.Generic.HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif"
    };

    public static bool IsImageFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public bool IsSingleImageFile
    {
        get
        {
            if (!IsFile) return false;
            var paths = ParsedFilePaths;
            if (paths.Length != 1 || !IsImageFilePath(paths[0]))
                return false;
            try
            {
                return File.Exists(paths[0]);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool HasImagePreview => IsImage || IsSingleImageFile;

    public string? EffectiveImagePath
    {
        get
        {
            if (IsImage) return ImagePath;
            if (IsSingleImageFile)
            {
                var paths = ParsedFilePaths;
                return paths.Length > 0 ? paths[0] : null;
            }
            return null;
        }
    }

    public string[] ParsedFilePaths
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FilePaths))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<string[]>(FilePaths);
                    if (result != null && result.Length > 0)
                        return result;
                }
                catch
                {
                }
            }

            if (Type == ClipboardType.File && !string.IsNullOrWhiteSpace(Text))
            {
                var lines = Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                    return lines;
            }

            return Array.Empty<string>();
        }
    }

    public string FileDisplayName
    {
        get
        {
            var paths = ParsedFilePaths;
            if (paths.Length == 0)
                return string.Empty;
            var first = Path.GetFileName(paths[0]);
            if (string.IsNullOrEmpty(first))
                first = paths[0];
            return paths.Length == 1 ? first : $"{first} 等 {paths.Length} 个文件";
        }
    }

    public string FilePathDisplay
    {
        get
        {
            var paths = ParsedFilePaths;
            return paths.Length > 0 ? string.Join(Environment.NewLine, paths) : string.Empty;
        }
    }

    public static string BuildPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(3)
                        .Select(l => l.Trim())
                        .ToList();

        if (lines.Count == 0)
            return string.Empty;

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length > 200)
                lines[i] = lines[i][..200] + "...";
        }

        var preview = string.Join(Environment.NewLine, lines);
        var totalLineCount = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
        if (totalLineCount > 3)
            preview += "...";

        return preview;
    }
}
