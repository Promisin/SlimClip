using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public class UpdateService : IUpdateService
{
    private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/Promisin/SlimClip/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SlimClip", "1.0"));
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiLatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var updateInfo = ParseRelease(doc.RootElement, isWin);
            if (updateInfo == null)
            {
                return UpdateCheckResult.Failed("未找到匹配当前平台的安装资产。");
            }

            if (IsNewerVersion(currentVersion, updateInfo.Version))
            {
                return UpdateCheckResult.Found(updateInfo);
            }

            return UpdateCheckResult.UpToDate();
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed("检查更新已取消。");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed($"网络连接失败：{ex.Message}");
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Update");
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, update.AssetName);

        // 如果文件已存在且大小完全匹配，可复用
        if (File.Exists(targetPath))
        {
            var existingInfo = new FileInfo(targetPath);
            if (update.AssetSize > 0 && existingInfo.Length == update.AssetSize)
            {
                progress?.Report(1.0);
                return targetPath;
            }
            File.Delete(targetPath);
        }

        using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;
            if (totalBytes > 0)
            {
                progress?.Report((double)totalRead / totalBytes);
            }
        }

        progress?.Report(1.0);
        return targetPath;
    }

    public void LaunchInstallerAndExit(string installerPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: 启动安装向导，让 Inno Setup 接管安装并关闭旧进程
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SP-", // 跳过“准备安装此程序吗”向导提示，直接进入安装流程
                UseShellExecute = true
            });
            Environment.Exit(0);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: 如果是 DMG，使用 hdiutil mount 挂载并打开 Finder 引导
            if (installerPath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start("open", $"\"{installerPath}\"");
                Environment.Exit(0);
            }
            else
            {
                Process.Start("open", $"\"{installerPath}\"");
                Environment.Exit(0);
            }
        }
    }

    public static bool IsNewerVersion(string current, string remote)
    {
        var curClean = CleanVersionString(current);
        var remClean = CleanVersionString(remote);

        if (Version.TryParse(curClean, out var curVer) && Version.TryParse(remClean, out var remVer))
        {
            return remVer > curVer;
        }

        return string.Compare(remClean, curClean, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static UpdateInfo? ParseRelease(JsonElement root, bool isWindows)
    {
        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
        var version = CleanVersionString(tagName);
        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? string.Empty : string.Empty;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string preferredExt = isWindows ? "-setup.exe" : "-macos-arm64.dmg";
        string fallbackExt = isWindows ? ".exe" : ".dmg";

        JsonElement? matchedAsset = null;

        // 优先匹配首选产物
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
            if (!string.IsNullOrEmpty(name) && name.EndsWith(preferredExt, StringComparison.OrdinalIgnoreCase))
            {
                matchedAsset = asset;
                break;
            }
        }

        // 兜底匹配
        if (matchedAsset == null)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                if (!string.IsNullOrEmpty(name) && name.EndsWith(fallbackExt, StringComparison.OrdinalIgnoreCase))
                {
                    matchedAsset = asset;
                    break;
                }
            }
        }

        if (matchedAsset == null)
        {
            return null;
        }

        var assetName = matchedAsset.Value.GetProperty("name").GetString() ?? string.Empty;
        var downloadUrl = matchedAsset.Value.GetProperty("browser_download_url").GetString() ?? string.Empty;
        var size = matchedAsset.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;

        return new UpdateInfo
        {
            Version = version,
            TagName = tagName,
            ReleaseNotes = body,
            AssetName = assetName,
            DownloadUrl = downloadUrl,
            AssetSize = size,
            HtmlUrl = htmlUrl
        };
    }

    private static string CleanVersionString(string ver)
    {
        if (string.IsNullOrWhiteSpace(ver)) return "0.0.0";
        var cleaned = ver.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(1);
        }
        return cleaned;
    }
}
