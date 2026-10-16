using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SlimClip.UI.Helpers;

public static class PlatformLauncher
{
    public static bool OpenUrl(string url)
    {
        try
        {
            using var proc = StartUrlProcess(url);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool OpenFolder(string path)
    {
        try
        {
            using var proc = StartFolderProcess(path);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }

    internal static Process? StartUrlProcess(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                return Process.Start(psi);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = File.Exists("/usr/bin/open") ? "/usr/bin/open" : "open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(url);
                return Process.Start(psi);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(url);
                return Process.Start(psi);
            }
        }
        catch { }

        return null;
    }

    internal static Process? StartFolderProcess(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                psi.ArgumentList.Add(path);
                return Process.Start(psi);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = File.Exists("/usr/bin/open") ? "/usr/bin/open" : "open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(path);
                return Process.Start(psi);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(path);
                return Process.Start(psi);
            }
        }
        catch { }

        return null;
    }
}
