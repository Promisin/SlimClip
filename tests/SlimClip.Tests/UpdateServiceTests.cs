using System;
using System.Text.Json;
using SlimClip.Core.Models;
using Xunit;

namespace SlimClip.Tests;

public class UpdateServiceTests
{
    private const string SampleReleaseJson = """
    {
      "tag_name": "v1.0.3",
      "name": "SlimClip v1.0.3",
      "body": "## 修复缺陷\n- 优化复制性能",
      "html_url": "https://github.com/Promisin/SlimClip/releases/tag/v1.0.3",
      "assets": [
        {
          "name": "SlimClip-v1.0.3-windows-x64-setup.exe",
          "browser_download_url": "https://github.com/Promisin/SlimClip/releases/download/v1.0.3/SlimClip-v1.0.3-windows-x64-setup.exe",
          "size": 15000000
        },
        {
          "name": "SlimClip-v1.0.3-macos-arm64.dmg",
          "browser_download_url": "https://github.com/Promisin/SlimClip/releases/download/v1.0.3/SlimClip-v1.0.3-macos-arm64.dmg",
          "size": 18000000
        }
      ]
    }
    """;

    [Theory]
    [InlineData("1.0.2", "1.0.3", true)]
    [InlineData("v1.0.2", "v1.0.3", true)]
    [InlineData("1.0.3", "1.0.3", false)]
    [InlineData("1.0.4", "1.0.3", false)]
    [InlineData("1.0.2.0", "1.0.3.0", true)]
    public void VersionComparison_DetectsNewerVersionCorrectly(string current, string remote, bool expectedNewer)
    {
        var isNewer = SlimClip.Core.Services.UpdateService.IsNewerVersion(current, remote);
        Assert.Equal(expectedNewer, isNewer);
    }

    [Fact]
    public void ParseReleaseJson_SelectsWindowsAsset_OnWindows()
    {
        using var doc = JsonDocument.Parse(SampleReleaseJson);
        var info = SlimClip.Core.Services.UpdateService.ParseRelease(doc.RootElement, isWindows: true);

        Assert.NotNull(info);
        Assert.Equal("1.0.3", info.Version);
        Assert.Equal("v1.0.3", info.TagName);
        Assert.Equal("SlimClip-v1.0.3-windows-x64-setup.exe", info.AssetName);
        Assert.Contains("setup.exe", info.DownloadUrl);
        Assert.Equal(15000000, info.AssetSize);
    }

    [Fact]
    public void ParseReleaseJson_SelectsMacAsset_OnMac()
    {
        using var doc = JsonDocument.Parse(SampleReleaseJson);
        var info = SlimClip.Core.Services.UpdateService.ParseRelease(doc.RootElement, isWindows: false);

        Assert.NotNull(info);
        Assert.Equal("1.0.3", info.Version);
        Assert.Equal("SlimClip-v1.0.3-macos-arm64.dmg", info.AssetName);
        Assert.Contains(".dmg", info.DownloadUrl);
        Assert.Equal(18000000, info.AssetSize);
    }
}
