namespace SlimClip.Core.Models;

public record StorageBreakdown
{
    public int TextCount { get; init; }
    public long TextBytes { get; init; }

    public int ImageCount { get; init; }
    public long ImageBytes { get; init; }

    public int FileCount { get; init; }
    public long FileBytes { get; init; }

    public int FavoriteCount { get; init; }

    public int TotalCount => TextCount + ImageCount + FileCount;
    public long TotalBytes => TextBytes + ImageBytes + FileBytes;

    public string FormattedTextBytes => FormatBytes(TextBytes);
    public string FormattedImageBytes => FormatBytes(ImageBytes);
    public string FormattedFileBytes => FormatBytes(FileBytes);
    public string FormattedTotalBytes => FormatBytes(TotalBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public enum CleanTimeRange
{
    All = 0,             // 全部时间
    OlderThanWeek = 7,   // 一周前 (7天)
    OlderThanMonth = 30, // 一个月前 (30天)
    OlderThanQuarter = 90 // 三个月前 (90天)
}

public record TargetedCleanFilter
{
    public bool CleanText { get; init; } = true;
    public bool CleanImages { get; init; } = true;
    public bool CleanFiles { get; init; } = true;
    public CleanTimeRange TimeRange { get; init; } = CleanTimeRange.All;
    public bool PreserveFavorites { get; init; } = true;
}
