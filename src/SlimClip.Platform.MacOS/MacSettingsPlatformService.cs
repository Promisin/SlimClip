using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

public class MacSettingsPlatformService : IPlatformSettingsHook
{
    private const string AppName = "SlimClip";
    private const string PlistLabel = "com.slimclip.app";

    public bool SetAutoStart(bool enabled)
    {
        try
        {
            var bundlePath = FindAppBundlePath();
            if (!string.IsNullOrEmpty(bundlePath))
            {
                // 1. 打包环境 (.app)：使用 AppleScript 注册到 macOS 系统的“在登录时打开 (Open at Login)”
                var success = SetLoginItem(bundlePath, enabled);
                // 清理可能残留的 LaunchAgent plist，避免双重注册
                RemoveLaunchAgentPlist();
                return success;
            }
            else
            {
                // 2. 裸二进制开发环境：回退到 ~/Library/LaunchAgents/
                if (enabled)
                {
                    var execPath = Environment.ProcessPath ?? string.Empty;
                    var success = WriteLaunchAgentPlist(execPath);
                    SetLoginItem(string.Empty, false);
                    return success;
                }
                else
                {
                    var success = RemoveLaunchAgentPlist();
                    SetLoginItem(string.Empty, false);
                    return success;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> RequestElevationRestartAsync()
    {
        // macOS does not have UAC elevation model
        return Task.FromResult(false);
    }

    public bool IsAutoStartEnabled()
    {
        try
        {
            // 无论是注册在系统登录项 (Login Items) 还是 LaunchAgents 守护代理，均能准确识别
            if (IsLoginItemRegistered())
            {
                return true;
            }
            return HasLaunchAgentPlist();
        }
        catch
        {
            return false;
        }
    }

    internal static string? ExtractAppBundlePath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath))
            return null;

        var appIndex = processPath.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
        if (appIndex >= 0)
        {
            return processPath.Substring(0, appIndex + 4);
        }
        return null;
    }

    internal static string? FindAppBundlePath(string? processPath = null)
    {
        var path = processPath ?? Environment.ProcessPath;
        var bundlePath = ExtractAppBundlePath(path);
        if (!string.IsNullOrEmpty(bundlePath) && Directory.Exists(bundlePath))
        {
            return bundlePath;
        }
        return null;
    }

    private static string GetLaunchAgentPlistPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents", $"{PlistLabel}.plist");
    }

    private static bool WriteLaunchAgentPlist(string execPath)
    {
        try
        {
            var plistPath = GetLaunchAgentPlistPath();
            var dir = Path.GetDirectoryName(plistPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>{PlistLabel}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{execPath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>";
            File.WriteAllText(plistPath, plistContent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RemoveLaunchAgentPlist()
    {
        try
        {
            var plistPath = GetLaunchAgentPlistPath();
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasLaunchAgentPlist()
    {
        try
        {
            return File.Exists(GetLaunchAgentPlistPath());
        }
        catch
        {
            return false;
        }
    }

    private static bool SetLoginItem(string bundlePath, bool enabled)
    {
        if (enabled)
        {
            var script = $"tell application \"System Events\"\n" +
                         $"    delete (every login item whose name is \"{AppName}\")\n" +
                         $"    make new login item at end with properties {{path:\"{bundlePath}\", hidden:false, name:\"{AppName}\"}}\n" +
                         $"end tell";
            var (ok, _) = RunAppleScript(script);
            return ok;
        }
        else
        {
            var script = $"tell application \"System Events\" to delete (every login item whose name is \"{AppName}\")";
            var (ok, _) = RunAppleScript(script);
            return ok;
        }
    }

    private static bool IsLoginItemRegistered()
    {
        var script = $"tell application \"System Events\" to get (count of (every login item whose name is \"{AppName}\")) > 0";
        var (ok, output) = RunAppleScript(script);
        return ok && bool.TryParse(output, out var exists) && exists;
    }

    internal static (bool success, string output) RunAppleScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return (false, string.Empty);

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return (false, string.Empty);
            }
            return (process.ExitCode == 0, output);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    public string? GetSystemPreferredLanguage()
    {
        // 1. 优先使用 Objective-C Runtime 高性能直接调用 [NSLocale preferredLanguages] (内存级调用，<0.1ms)
        try
        {
            var nsLocaleClass = MacNativeMethods.objc_getClass("NSLocale");
            if (nsLocaleClass != IntPtr.Zero)
            {
                var selPreferred = MacNativeMethods.sel_registerName("preferredLanguages");
                var languagesArray = MacNativeMethods.objc_msgSend(nsLocaleClass, selPreferred);
                if (languagesArray != IntPtr.Zero)
                {
                    var selFirstObject = MacNativeMethods.sel_registerName("firstObject");
                    var firstObj = MacNativeMethods.objc_msgSend(languagesArray, selFirstObject);
                    if (firstObj != IntPtr.Zero)
                    {
                        var selUtf8 = MacNativeMethods.sel_registerName("UTF8String");
                        var utf8Ptr = MacNativeMethods.objc_msgSend(firstObj, selUtf8);
                        if (utf8Ptr != IntPtr.Zero)
                        {
                            var langStr = Marshal.PtrToStringUTF8(utf8Ptr);
                            if (!string.IsNullOrWhiteSpace(langStr))
                            {
                                return langStr;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. 回退机制：直接从 macOS 用户首选项数据库读取 AppleLanguages
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "defaults",
                ArgumentList = { "read", "-g", "AppleLanguages" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);
                var match = System.Text.RegularExpressions.Regex.Match(output, "\"([^\"]+)\"");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch { }

        return null;
    }
}
