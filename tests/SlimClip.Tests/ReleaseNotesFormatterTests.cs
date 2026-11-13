using System;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class ReleaseNotesFormatterTests
{
    [Fact]
    public void Format_NullOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, ReleaseNotesFormatter.Format(null));
        Assert.Equal(string.Empty, ReleaseNotesFormatter.Format(""));
        Assert.Equal(string.Empty, ReleaseNotesFormatter.Format("   \n\t  "));
    }

    [Fact]
    public void Format_CleansChangelogHeadersAndVersionLine()
    {
        var raw = """
        # 更新日志 (Changelog)

        本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 格式与 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/) 规范。

        ## [1.0.6] - 2026-11-11

        ### 新增
        - **主题风格设置**：设置外观页新增“主题风格”切换选项。

        ### 修复
        - **Windows 10 窗口尖角阴影**：修复直角阴影问题。
        """;

        var formatted = ReleaseNotesFormatter.Format(raw);

        // 验证去掉了顶部的 Changelog 介绍与 [1.0.6] 冗余版本头
        Assert.DoesNotContain("# 更新日志", formatted);
        Assert.DoesNotContain("Keep a Changelog", formatted);
        Assert.DoesNotContain("[1.0.6]", formatted);

        // 验证章节被转化为规范中文章节
        Assert.Contains("【新增】", formatted);
        Assert.Contains("【修复】", formatted);

        // 验证 Markdown 加粗标记被移除
        Assert.DoesNotContain("**", formatted);
        Assert.Contains("• 主题风格设置：设置外观页新增“主题风格”切换选项。", formatted);
        Assert.Contains("• Windows 10 窗口尖角阴影：修复直角阴影问题。", formatted);
    }

    [Fact]
    public void Format_SimpleMarkdown_ConvertsBulletsAndStripsFormatting()
    {
        var raw = """
        ## 核心亮点
        - **极简设计**：界面简洁，功能聚焦。
        - **本地运行**：无云端依赖，支持 `SQLite`。
        * 访问 [官方网站](https://example.com) 查看文档。
        """;

        var formatted = ReleaseNotesFormatter.Format(raw);

        Assert.Contains("【核心亮点】", formatted);
        Assert.Contains("• 极简设计：界面简洁，功能聚焦。", formatted);
        Assert.Contains("• 本地运行：无云端依赖，支持 SQLite。", formatted);
        Assert.Contains("• 访问 官方网站 查看文档。", formatted);
        Assert.DoesNotContain("**", formatted);
        Assert.DoesNotContain("`", formatted);
        Assert.DoesNotContain("https://example.com", formatted);
    }

    [Fact]
    public void Format_PlainText_RemainsReadable()
    {
        var raw = "本次更新优化了窗口动画和响应速度，欢迎体验。";
        var formatted = ReleaseNotesFormatter.Format(raw);
        Assert.Equal("本次更新优化了窗口动画和响应速度，欢迎体验。", formatted);
    }

    [Fact]
    public void UpdateInfo_FormattedReleaseNotes_ReturnsCleanedText()
    {
        var info = new UpdateInfo
        {
            Version = "1.0.6",
            TagName = "v1.0.6",
            ReleaseNotes = "### 新增\n- **功能**：描述",
            AssetName = "SlimClip-setup.exe",
            DownloadUrl = "https://example.com/download",
            AssetSize = 1000,
            HtmlUrl = "https://example.com"
        };

        Assert.Equal("【新增】" + Environment.NewLine + "• 功能：描述", info.FormattedReleaseNotes);
    }
}
