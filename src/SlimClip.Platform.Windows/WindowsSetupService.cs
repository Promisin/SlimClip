using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsSetupService : IWindowsSetupService
{
    private const string UninstallBaseKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string NewUninstallKeyName = "SlimClip";
    private const string LegacyInnoKeyName = "{67EF0B38-FFC8-4D35-B1E3-FFEC27AE6503}_is1";
    private const string ExplorerAdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DisabledHotkeysValueName = "DisabledHotkeys";

    private readonly ILocalizationService _localizationService;

    public WindowsSetupService(ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService ?? new LocalizationService();
    }

    public string? DetectExistingInstallPath()
    {
        string[] subKeys =
        [
            $"{UninstallBaseKey}\\{NewUninstallKeyName}",
            $"{UninstallBaseKey}\\{LegacyInnoKeyName}"
        ];

        RegistryHive[] hives = [RegistryHive.LocalMachine, RegistryHive.CurrentUser];
        RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32];

        foreach (var subKey in subKeys)
        {
            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var key = baseKey.OpenSubKey(subKey, writable: false);
                        if (key == null) continue;

                        var rawLoc = key.GetValue("InstallLocation") as string
                                     ?? key.GetValue("Inno Setup: App Path") as string;

                        if (!string.IsNullOrWhiteSpace(rawLoc))
                        {
                            var clean = rawLoc.Trim().Trim('"').TrimEnd('\\', '/');
                            if (Directory.Exists(clean) && File.Exists(Path.Combine(clean, "SlimClip.exe")))
                            {
                                return clean;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        return null;
    }

    public async Task InstallAsync(InstallOptions options, IProgress<SetupProgress>? progress = null)
    {
        await Task.Run(async () =>
        {
            progress?.Report(new SetupProgress(10, _localizationService["Setup_Status_StoppingOld"]));
            TerminateOtherSlimClipInstances();
            await Task.Delay(180);

            string targetDir = options.TargetDirectory.TrimEnd('\\', '/');
            Directory.CreateDirectory(targetDir);

            progress?.Report(new SetupProgress(30, _localizationService["Setup_Status_CopyingFiles"]));
            CopyPayloadFiles(options.SourceDirectory, targetDir, progress);

            // 清理旧版 Inno Setup 遗留的 unins000.* 文件
            CleanupLegacyInnoUninstallerFiles(targetDir);

            string installedExePath = Path.Combine(targetDir, "SlimClip.exe");

            progress?.Report(new SetupProgress(80, _localizationService["Setup_Status_ConfiguringSystem"]));

            if (!options.IsUpgradeMode)
            {
                ConfigureAutoStartAndSettings(targetDir, installedExePath, options.EnableAutoStart);

                if (options.CreateDesktopShortcut)
                {
                    TryCreateDesktopShortcut(installedExePath);
                }
            }

            TryCreateStartMenuShortcut(installedExePath);
            RegisterUninstallEntry(targetDir, installedExePath, options.AppVersion);

            await Task.Delay(150);
            progress?.Report(new SetupProgress(100, _localizationService["Setup_Status_Completed"]));
        });
    }

    public async Task UninstallAsync(string installDirectory, IProgress<SetupProgress>? progress = null)
    {
        await Task.Run(async () =>
        {
            progress?.Report(new SetupProgress(15, _localizationService["Setup_Status_StoppingOld"]));
            TerminateOtherSlimClipInstances();
            await Task.Delay(200);

            progress?.Report(new SetupProgress(45, _localizationService["Setup_Status_CleaningSystem"]));
            var settingsPlatform = new WindowsSettingsPlatformService();
            settingsPlatform.SetAutoStart(false);

            RestoreManagedDisabledHotkeys(installDirectory);
            RemoveShortcuts();
            RemoveUninstallRegistryEntries();

            progress?.Report(new SetupProgress(80, _localizationService["Setup_Status_RemovingFiles"]));
            DeleteInstallDirectoryDeferred(installDirectory);

            await Task.Delay(150);
            progress?.Report(new SetupProgress(100, _localizationService["Setup_Status_Completed"]));
        });
    }

    public void LaunchInstalledApp(string installDirectory)
    {
        try
        {
            string exePath = Path.Combine(installDirectory, "SlimClip.exe");
            if (!File.Exists(exePath)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installDirectory,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static void TerminateOtherSlimClipInstances()
    {
        int currentPid = Environment.ProcessId;
        try
        {
            foreach (var proc in Process.GetProcessesByName("SlimClip"))
            {
                try
                {
                    if (proc.Id != currentPid)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
    }

    private void CopyPayloadFiles(string sourceDir, string targetDir, IProgress<SetupProgress>? progress)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"未找到安装源目录: {sourceDir}");

        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        int total = Math.Max(1, files.Length);

        for (int i = 0; i < files.Length; i++)
        {
            string srcFile = files[i];
            string relative = Path.GetRelativePath(sourceDir, srcFile);
            string fileName = Path.GetFileName(srcFile);

            // 跳过开发或临时数据文件，避免覆盖目标目录已有的用户数据
            if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destFile = Path.Combine(targetDir, relative);
            string? destSubDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destSubDir))
            {
                Directory.CreateDirectory(destSubDir);
            }

            // 若自解压或同目录调试时源与目标完全一致，跳过复制
            if (!string.Equals(Path.GetFullPath(srcFile), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(srcFile, destFile, overwrite: true);
            }

            double pct = 30 + ((i + 1.0) / total) * 45.0;
            progress?.Report(new SetupProgress(pct, _localizationService["Setup_Status_CopyingFiles"]));
        }
    }

    private static void CleanupLegacyInnoUninstallerFiles(string targetDir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(targetDir, "unins000.*"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    private static void ConfigureAutoStartAndSettings(string targetDir, string installedExePath, bool enableAutoStart)
    {
        try
        {
            // 1. 配置计划任务或注册表自启（指向目标安装目录的 SlimClip.exe）
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                key?.DeleteValue("SlimClip", throwOnMissingValue: false);
            }

            if (enableAutoStart)
            {
                RunSchTasks($"/create /tn \"SlimClip\" /tr \"\\\"{installedExePath}\\\"\" /sc onlogon /rl highest /f");
            }
            else
            {
                RunSchTasks("/delete /tn \"SlimClip\" /f");
            }

            // 2. 同步写入目标目录 settings.json 的 StartWithSystem
            string settingsPath = Path.Combine(targetDir, "settings.json");
            AppSettings settings = new();
            if (File.Exists(settingsPath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
                    if (existing != null) settings = existing;
                }
                catch { }
            }

            settings.StartWithSystem = enableAutoStart;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);
        }
        catch { }
    }

    private static void RunSchTasks(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit(3000);
        }
        catch { }
    }

    private static void TryCreateDesktopShortcut(string targetExePath)
    {
        try
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktopDir))
            {
                CreateShortcut(Path.Combine(desktopDir, "SlimClip.lnk"), targetExePath, "SlimClip 剪贴板管理工具");
            }
        }
        catch { }
    }

    private static void TryCreateStartMenuShortcut(string targetExePath)
    {
        try
        {
            string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!string.IsNullOrWhiteSpace(startMenuDir))
            {
                CreateShortcut(Path.Combine(startMenuDir, "SlimClip.lnk"), targetExePath, "SlimClip 剪贴板管理工具");
            }
        }
        catch { }
    }

    private static void RemoveShortcuts()
    {
        string[] dirs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        ];

        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string lnk = Path.Combine(dir, "SlimClip.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
        }
    }

    private static void RegisterUninstallEntry(string targetDir, string installedExePath, string version)
    {
        try
        {
            // 清理旧版 Inno Setup 卸载键
            RemoveKeyIfExists(RegistryHive.LocalMachine, RegistryView.Registry64, $"{UninstallBaseKey}\\{LegacyInnoKeyName}");
            RemoveKeyIfExists(RegistryHive.LocalMachine, RegistryView.Registry32, $"{UninstallBaseKey}\\{LegacyInnoKeyName}");
            RemoveKeyIfExists(RegistryHive.CurrentUser, RegistryView.Registry64, $"{UninstallBaseKey}\\{LegacyInnoKeyName}");

            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey($"{UninstallBaseKey}\\{NewUninstallKeyName}", writable: true)
                            ?? Registry.CurrentUser.CreateSubKey($"{UninstallBaseKey}\\{NewUninstallKeyName}", writable: true);

            if (key == null) return;

            key.SetValue("DisplayName", "SlimClip", RegistryValueKind.String);
            key.SetValue("DisplayVersion", version, RegistryValueKind.String);
            key.SetValue("Publisher", "Promisin", RegistryValueKind.String);
            key.SetValue("InstallLocation", targetDir, RegistryValueKind.String);
            key.SetValue("DisplayIcon", installedExePath, RegistryValueKind.String);
            key.SetValue("UninstallString", $"\"{installedExePath}\" --uninstall", RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void RemoveUninstallRegistryEntries()
    {
        string[] subKeys =
        [
            $"{UninstallBaseKey}\\{NewUninstallKeyName}",
            $"{UninstallBaseKey}\\{LegacyInnoKeyName}"
        ];
        RegistryHive[] hives = [RegistryHive.LocalMachine, RegistryHive.CurrentUser];
        RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32];

        foreach (var subKey in subKeys)
        {
            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    RemoveKeyIfExists(hive, view, subKey);
                }
            }
        }
    }

    private static void RemoveKeyIfExists(RegistryHive hive, RegistryView view, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private static void RestoreManagedDisabledHotkeys(string installDir)
    {
        try
        {
            string settingsPath = Path.Combine(installDir, "settings.json");
            if (!File.Exists(settingsPath)) return;

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
            var managedChar = settings?.ManagedDisabledHotkeyChar;
            if (string.IsNullOrEmpty(managedChar)) return;

            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKeyPath, writable: true);
            if (key == null) return;

            var current = key.GetValue(DisabledHotkeysValueName) as string;
            if (string.IsNullOrEmpty(current)) return;

            var updated = current.Replace(managedChar, string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(updated))
            {
                key.DeleteValue(DisabledHotkeysValueName, throwOnMissingValue: false);
            }
            else if (updated != current)
            {
                key.SetValue(DisabledHotkeysValueName, updated, RegistryValueKind.String);
            }
        }
        catch { }
    }

    private static void DeleteInstallDirectoryDeferred(string installDir)
    {
        try
        {
            if (!Directory.Exists(installDir)) return;

            // 启动后台延迟清理进程，待当前卸载进程退出后移除目录
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C ping 127.0.0.1 -n 3 > nul & rd /s /q \"{installDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }

    private static void CreateShortcut(string shortcutPath, string targetExePath, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetExePath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetExePath) ?? string.Empty);
        link.SetDescription(description);
        link.SetIconLocation(targetExePath, 0);

        var file = (IPersistFile)link;
        file.Save(shortcutPath, true);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
