using System;
using System.IO;

namespace SlimClip.Core.Helpers;

public static class InstallerPathHelper
{
    public const string DefaultAppFolderName = "SlimClip";

    public static string GetDefaultInstallDirectory(string appFolderName = DefaultAppFolderName)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            programFiles = @"C:\Program Files";
        }
        return Path.Combine(programFiles, appFolderName);
    }

    /// <summary>
    /// 规范化用户选择或输入的安装目录：
    /// 自动判断末尾是否包含 SlimClip 文件夹后缀，若不包含（例如选了磁盘根目录 D:\ 或父目录 D:\Apps），
    /// 自动补齐 \SlimClip 后缀并返回规范路径，防止应用文件被直接铺散到磁盘根目录或公共目录。
    /// </summary>
    public static string NormalizeInstallPath(string? rawPath, string appFolderName = DefaultAppFolderName)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return GetDefaultInstallDirectory(appFolderName);
        }

        string cleaned = rawPath.Trim().Trim('"').Replace('/', '\\');

        // 处理盘符如 "D:" -> "D:\"
        if (cleaned.Length == 2 && char.IsLetter(cleaned[0]) && cleaned[1] == ':')
        {
            cleaned += "\\";
        }

        // 去除非根目录的尾部反斜杠（保留 "D:\" 长度为 3 的根目录格式）
        while (cleaned.Length > 3 && cleaned.EndsWith('\\'))
        {
            cleaned = cleaned[..^1];
        }

        // 若是根目录（如 "D:\" 或 "\"），直接拼接 SlimClip
        if (cleaned.Length <= 3 && cleaned.EndsWith('\\'))
        {
            return cleaned + appFolderName;
        }

        int lastSep = cleaned.LastIndexOf('\\');
        string lastSegment = lastSep >= 0 ? cleaned[(lastSep + 1)..] : cleaned;

        if (string.Equals(lastSegment, appFolderName, StringComparison.OrdinalIgnoreCase))
        {
            // 统一末尾文件夹名称大小写为标准名称
            string prefix = lastSep >= 0 ? cleaned[..(lastSep + 1)] : string.Empty;
            return prefix + appFolderName;
        }

        return cleaned + "\\" + appFolderName;
    }
}
