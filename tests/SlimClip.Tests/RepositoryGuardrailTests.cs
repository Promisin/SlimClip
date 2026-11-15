using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SlimClip.Tests;

public class RepositoryGuardrailTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SlimClip.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root containing SlimClip.slnx");
    }

    [Fact]
    public void Repository_TextFiles_MustNotContain_DoubleCarriageReturns_Or_OrphanCR()
    {
        var root = FindRepoRoot();
        string[] binaryExtensions = [".png", ".ico", ".icns", ".ttf", ".exe", ".dll", ".pdb", ".zip", ".dmg", ".db", ".db-shm", ".db-wal", ".log"];
        string[] ignoredDirs = ["bin", "obj", ".git", ".vs", ".vscode", ".scratch", "releases", "images", ".publish-staging", "dist", "TestResults"];

        var violations = new List<string>();

        IEnumerable<string> EnumerateFilesSafely(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                if (ignoredDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (var subFile in EnumerateFilesSafely(subDir))
                {
                    yield return subFile;
                }
            }
        }

        foreach (var filePath in EnumerateFilesSafely(root))
        {
            var ext = Path.GetExtension(filePath);
            if (binaryExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(filePath);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\r')
                {
                    // Check for \r\r\n or orphan \r not followed by \n
                    if (i + 1 >= bytes.Length || bytes[i + 1] != (byte)'\n')
                    {
                        var rel = Path.GetRelativePath(root, filePath);
                        violations.Add($"{rel} (orphan CR or CR+CR at byte {i})");
                        break;
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Found malformed line endings (CR not immediately followed by LF, e.g. \\r\\r\\n):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Localization_Dictionaries_MustHave_IdenticalKeys_And_NonEmptyValues()
    {
        var root = FindRepoRoot();
        var zhPath = Path.Combine(root, "src", "SlimClip.Core", "Assets", "Locales", "zh-CN.json");
        var enPath = Path.Combine(root, "src", "SlimClip.Core", "Assets", "Locales", "en-US.json");

        Assert.True(File.Exists(zhPath), "zh-CN.json not found");
        Assert.True(File.Exists(enPath), "en-US.json not found");

        var zhDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(zhPath));
        var enDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(enPath));

        Assert.NotNull(zhDict);
        Assert.NotNull(enDict);

        var missingInEn = zhDict.Keys.Except(enDict.Keys).ToList();
        var missingInZh = enDict.Keys.Except(zhDict.Keys).ToList();

        Assert.True(missingInEn.Count == 0, $"Keys present in zh-CN.json but missing in en-US.json: {string.Join(", ", missingInEn)}");
        Assert.True(missingInZh.Count == 0, $"Keys present in en-US.json but missing in zh-CN.json: {string.Join(", ", missingInZh)}");

        var emptyZh = zhDict.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        var emptyEn = enDict.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();

        Assert.True(emptyZh.Count == 0, $"Empty values in zh-CN.json: {string.Join(", ", emptyZh)}");
        Assert.True(emptyEn.Count == 0, $"Empty values in en-US.json: {string.Join(", ", emptyEn)}");
    }

    [Fact]
    public void SyncToPublicScript_MustInclude_GitAttributes_DynamicVersion_And_LineEndingNormalization()
    {
        var root = FindRepoRoot();
        var scriptPath = Path.Combine(root, "sync-to-public.ps1");
        if (!File.Exists(scriptPath))
        {
            // public 开源仓库不包含开发侧同步脚本，直接跳过此项脚本自检
            return;
        }

        var content = File.ReadAllText(scriptPath);
        Assert.Contains("\".gitattributes\"", content);
        Assert.Contains("Directory.Build.props", content);
        Assert.Contains(".Replace(\"`r`n\", \"`n\")", content);
        Assert.DoesNotContain("[string]$Version = \"1.0.5\"", content);
    }
}
