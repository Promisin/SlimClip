using System;
using System.Threading;
using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default);

    Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    void LaunchInstallerAndExit(string installerPath);
}
