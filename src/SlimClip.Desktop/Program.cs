using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

using SlimClip.UI.Fonts;

namespace SlimClip.Desktop;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SlimClip.Platform.MacOS.MacLiquidGlassHelper.ConfigureAccessoryApp();
        }

        bool isSetupOrUninstall = args.Any(a =>
            string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

        // 卸载模式下，若当前进程位于被卸载目录，先复制到 %TEMP% 接力执行以释放安装目录文件锁
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)) &&
            !args.Any(a => string.Equals(a, "--temp-relaunch", StringComparison.OrdinalIgnoreCase)))
        {
            if (TryRelaunchUninstallerFromTemp())
            {
                return;
            }
        }

        if (!isSetupOrUninstall)
        {
            var mutexName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"Local\SlimClip_SingleInstance_Mutex"
                : "SlimClip_SingleInstance_Mutex";
            bool acquired = false;
            try
            {
                _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
                acquired = createdNew;
                if (!acquired)
                {
                    acquired = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(2), false);
                }
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return;
            }
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex?.Dispose();
        }
    }

    private static bool TryRelaunchUninstallerFromTemp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            var installDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            var tempUninstaller = Path.Combine(Path.GetTempPath(), $"SlimClip_Uninstall_{Guid.NewGuid():N}.exe");
            File.Copy(exePath, tempUninstaller, overwrite: true);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempUninstaller,
                Arguments = $"--uninstall --temp-relaunch --source \"{installDir}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithHarmonyOSFont()
            .WithInterFont()
            .LogToTrace();
}
