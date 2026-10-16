namespace SlimClip.Core.Models;

public record UpdateInfo
{
    public required string Version { get; init; }
    public required string TagName { get; init; }
    public string? ReleaseNotes { get; init; }
    public required string AssetName { get; init; }
    public required string DownloadUrl { get; init; }
    public long AssetSize { get; init; }
    public required string HtmlUrl { get; init; }
}

public record UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public UpdateInfo? Update { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate() => new() { HasUpdate = false };
    public static UpdateCheckResult Found(UpdateInfo info) => new() { HasUpdate = true, Update = info };
    public static UpdateCheckResult Failed(string error) => new() { HasUpdate = false, ErrorMessage = error };
}
