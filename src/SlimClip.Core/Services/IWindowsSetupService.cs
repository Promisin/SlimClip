using System;
using System.Threading.Tasks;

namespace SlimClip.Core.Services;

public sealed class InstallOptions
{
    public string SourceDirectory { get; init; } = string.Empty;
    public string TargetDirectory { get; init; } = string.Empty;
    public bool IsUpgradeMode { get; init; }
    public bool EnableAutoStart { get; init; } = true;
    public bool CreateDesktopShortcut { get; init; } = true;
    public string AppVersion { get; init; } = "1.0.5";
}

public readonly record struct SetupProgress(double Percent, string StatusText);

public interface IWindowsSetupService
{
    /// <summary>
    /// 检测当前设备上是否已安装过 SlimClip，若已安装则返回上次的安装路径，否则返回 null。
    /// </summary>
    string? DetectExistingInstallPath();

    /// <summary>
    /// 执行安装或覆盖更新。
    /// </summary>
    Task InstallAsync(InstallOptions options, IProgress<SetupProgress>? progress = null);

    /// <summary>
    /// 执行卸载。
    /// </summary>
    Task UninstallAsync(string installDirectory, IProgress<SetupProgress>? progress = null);

    /// <summary>
    /// 启动已安装的 SlimClip 主程序。
    /// </summary>
    void LaunchInstalledApp(string installDirectory);
}
