using System;
using System.IO;
using System.Threading.Tasks;
using SlimClip.Core.Services;
using Xunit;

#if !WINDOWS
using SlimClip.Platform.MacOS;
#endif

namespace SlimClip.Tests;

public class MacSettingsPlatformServiceTests
{
#if !WINDOWS
    [Theory]
    [InlineData("/Applications/SlimClip.app/Contents/MacOS/SlimClip", "/Applications/SlimClip.app")]
    [InlineData("/Users/test/Downloads/SlimClip.app/Contents/MacOS/SlimClip", "/Users/test/Downloads/SlimClip.app")]
    [InlineData("/Applications/Test App.app/Contents/MacOS/Test App", "/Applications/Test App.app")]
    [InlineData("/Applications/SlimClip.APP/Contents/MacOS/SlimClip", "/Applications/SlimClip.APP")]
    public void ExtractAppBundlePath_ValidBundlePath_ReturnsBundleDirectory(string input, string expected)
    {
        var result = MacSettingsPlatformService.ExtractAppBundlePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/Users/test/bin/SlimClip")]
    [InlineData("/usr/local/bin/slimclip")]
    [InlineData("C:\\Program Files\\SlimClip\\SlimClip.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractAppBundlePath_NonBundlePath_ReturnsNull(string? input)
    {
        var result = MacSettingsPlatformService.ExtractAppBundlePath(input);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestElevationRestartAsync_OnMacOS_ReturnsFalse()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var service = new MacSettingsPlatformService();
        var result = await service.RequestElevationRestartAsync();
        Assert.False(result);
    }

    [Fact]
    public void MacSettingsPlatformService_ReturnsValidSystemLanguage()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) return;

        var macHook = new MacSettingsPlatformService();
        var sysLang = macHook.GetSystemPreferredLanguage();
        Assert.NotNull(sysLang);
        Assert.NotEmpty(sysLang);
        var detected = LocalizationService.DetectDefaultLanguage(sysLang);
        Assert.True(detected == "zh-CN" || detected == "en-US", $"Expected zh-CN or en-US, got {detected}");
    }
#endif

}
