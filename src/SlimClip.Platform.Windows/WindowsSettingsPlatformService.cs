using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsSettingsPlatformService : IPlatformSettingsHook
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SlimClip";

    public bool SetAutoStart(bool enabled)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var isAdmin = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (enabled)
            {
                if (string.IsNullOrEmpty(exePath)) return false;

                if (isAdmin)
                {
                    // Remove registry entry if present
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        key?.DeleteValue(AppName, false);
                    }

                    // Create highest privilege task to bypass UAC on logon
                    return RunSchTasks($"/create /tn \"{AppName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f");
                }
                else
                {
                    // Remove scheduled task
                    RunSchTasks($"/delete /tn \"{AppName}\" /f");

                    // Standard HKCU run key
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                    if (key != null)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        return true;
                    }
                }
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    key?.DeleteValue(AppName, false);
                }
                RunSchTasks($"/delete /tn \"{AppName}\" /f");
                return true;
            }
        }
        catch { }

        return false;
    }

    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key?.GetValue(AppName) != null)
            {
                return true;
            }

            return RunSchTasks($"/query /tn \"{AppName}\"");
        }
        catch
        {
            return false;
        }
    }

    private static bool RunSchTasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> RequestElevationRestartAsync()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return Task.FromResult(false);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
            return Task.FromResult(true);
        }
        catch (Win32Exception)
        {
            // User rejected UAC dialog
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public string? GetSystemPreferredLanguage()
    {
        try
        {
            return System.Globalization.CultureInfo.InstalledUICulture.Name;
        }
        catch
        {
            return null;
        }
    }
}
