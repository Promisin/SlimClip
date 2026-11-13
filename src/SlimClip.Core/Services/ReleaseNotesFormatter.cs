namespace SlimClip.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static partial class ReleaseNotesFormatter
{
    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^\)]+\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*([^*]+)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    /// <summary>
    /// 将 GitHub Releases 或 Markdown 格式的版本日志清洗为适合界面展示的规整纯文本。
    /// </summary>
    public static string Format(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        using var reader = new StringReader(markdown);
        var lines = new List<string>();
        string? rawLine;
        bool hasEncounteredSection = false;

        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.Trim();

            // 过滤顶部的 Changelog / 更新日志 标题与前置规范说明
            if (!hasEncounteredSection)
            {
                if (line.StartsWith("# 更新日志", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("# Changelog", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("keepachangelog", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("semver", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 过滤类似 ## [1.0.6] - 2026-11-11 或 ## v1.0.6 的当前版本标题行
                if (line.StartsWith("## [", StringComparison.OrdinalIgnoreCase) ||
                    (line.StartsWith("## v", StringComparison.OrdinalIgnoreCase) && line.Length > 4 && char.IsDigit(line[4])))
                {
                    continue;
                }
            }

            if (string.IsNullOrEmpty(line))
            {
                if (lines.Count > 0 && lines[^1] != string.Empty)
                {
                    lines.Add(string.Empty);
                }
                continue;
            }

            hasEncounteredSection = true;

            // 处理标题行 (### 或 ## 或 #)
            if (line.StartsWith("### ", StringComparison.Ordinal) ||
                line.StartsWith("## ", StringComparison.Ordinal) ||
                line.StartsWith("# ", StringComparison.Ordinal))
            {
                var title = line.TrimStart('#').Trim();
                title = CleanInlineMarkdown(title);
                if (!string.IsNullOrEmpty(title))
                {
                    // 若标题尚未被【】包裹，包装成【title】
                    if (!title.StartsWith("【") && !title.StartsWith("["))
                    {
                        title = $"【{title}】";
                    }
                    lines.Add(title);
                }
                continue;
            }

            // 处理列表条目 (- 或 * 或 +)
            if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            {
                var content = line.Substring(2).Trim();
                content = CleanInlineMarkdown(content);
                lines.Add($"• {content}");
                continue;
            }

            // 处理普通正文行
            lines.Add(CleanInlineMarkdown(line));
        }

        // 移除末尾空行
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CleanInlineMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 1. 去除图片
        text = MarkdownImageRegex().Replace(text, string.Empty);

        // 2. 转换超链接 [text](url) -> text
        text = MarkdownLinkRegex().Replace(text, "$1");

        // 3. 去除粗体 **text** -> text
        text = BoldRegex().Replace(text, "$1");

        // 4. 去除行内代码 `code` -> code
        text = InlineCodeRegex().Replace(text, "$1");

        // 5. 去除斜体 *text* -> text
        text = ItalicRegex().Replace(text, "$1");

        return text.Trim();
    }
}
