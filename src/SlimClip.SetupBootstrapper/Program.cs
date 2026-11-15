using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace SlimClip.SetupBootstrapper;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool autoUpdate = Array.Exists(args, a => string.Equals(a, "--auto-update", StringComparison.OrdinalIgnoreCase));
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"SlimClip_Setup_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempExtractDir);

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SlimClip.Payload.zip");
            if (stream == null)
            {
                return 1;
            }

            var zipPath = Path.Combine(tempExtractDir, "payload.zip");
            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(fs);
            }

            ZipFile.ExtractToDirectory(zipPath, tempExtractDir, overwriteFiles: true);
            File.Delete(zipPath);

            var mainExe = Path.Combine(tempExtractDir, "SlimClip.exe");
            if (!File.Exists(mainExe))
            {
                return 2;
            }

            var setupArgs = autoUpdate
                ? $"--setup --source \"{tempExtractDir}\" --auto-update"
                : $"--setup --source \"{tempExtractDir}\"";

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = mainExe,
                Arguments = setupArgs,
                WorkingDirectory = tempExtractDir,
                UseShellExecute = false
            });

            proc?.WaitForExit();
            return proc?.ExitCode ?? 0;
        }
        catch
        {
            return 3;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
