using System;
using System.Collections.Generic;
using System.IO;

namespace SlimClip.Core.Services;

public static class FileExistenceValidator
{
    public static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        var path = rawPath.Trim();
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                return uri.LocalPath;
            }
        }

        return path;
    }

    public static (List<string> Existing, List<string> Missing) Partition(IEnumerable<string> paths)
    {
        var existing = new List<string>();
        var missing = new List<string>();

        foreach (var raw in paths)
        {
            var normalized = NormalizePath(raw);
            if (string.IsNullOrEmpty(normalized))
                continue;

            if (File.Exists(normalized) || Directory.Exists(normalized))
            {
                existing.Add(normalized);
            }
            else
            {
                missing.Add(normalized);
            }
        }

        return (existing, missing);
    }
}
