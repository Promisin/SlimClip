using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Helpers;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class Windows10ChromeTests
{
    public Windows10ChromeTests()
    {
        StyleTests.EnsureHeadlessApp();
    }

    [Theory]
    [InlineData(true, 10, 0, 19045, true)]   // Windows 10 22H2
    [InlineData(true, 10, 0, 18363, true)]   // Windows 10 1909
    [InlineData(true, 10, 0, 21999, true)]   // Pre-Win11
    [InlineData(true, 10, 0, 22000, false)]  // Windows 11 21H2
    [InlineData(true, 10, 0, 22631, false)]  // Windows 11 23H2
    [InlineData(true, 10, 0, 26100, false)]  // Windows 11 24H2
    [InlineData(false, 14, 5, 0, false)]     // macOS Sonoma
    public void IsWindows10Build_CorrectlyClassifiesOsGeneration(
        bool isWindows, int major, int minor, int build, bool expectedIsWin10)
    {
        var result = WindowsPlatformChromeHelper.IsWindows10Build(isWindows, new Version(major, minor, build));
        Assert.Equal(expectedIsWin10, result);
    }

    [Fact]
    public void CalculateChromeMetrics_OnWindows10_Translucent_KeepsAcrylicAndUsesZeroCornerRadius()
    {
        var metrics = WindowsPlatformChromeHelper.CalculateChromeMetrics(
            isWindows: true,
            isWin10: true,
            backdropStyle: AppBackdropStyle.Translucent,
            baseWidth: 420,
            baseHeight: 560,
            minWidth: 340,
            minHeight: 400,
            maxWidth: 800,
            maxHeight: 1200);

        Assert.Equal(WindowDecorations.BorderOnly, metrics.Decorations);
        Assert.Contains(WindowTransparencyLevel.AcrylicBlur, metrics.TransparencyLevels);
        Assert.Equal(new CornerRadius(0), metrics.WindowCornerRadius);
        Assert.Equal(new Thickness(0), metrics.ShadowMargin);
        Assert.Equal(420, metrics.Width);
        Assert.Equal(560, metrics.Height);
    }

    [Fact]
    public void CalculateChromeMetrics_OnWindows10_Solid_KeepsRoundedCornersAndCompensatesShadowMargin()
    {
        var metrics = WindowsPlatformChromeHelper.CalculateChromeMetrics(
            isWindows: true,
            isWin10: true,
            backdropStyle: AppBackdropStyle.Solid,
            baseWidth: 420,
            baseHeight: 560,
            minWidth: 340,
            minHeight: 400,
            maxWidth: 800,
            maxHeight: 1200);

        Assert.Equal(WindowDecorations.None, metrics.Decorations);
        Assert.Single(metrics.TransparencyLevels);
        Assert.Equal(WindowTransparencyLevel.Transparent, metrics.TransparencyLevels[0]);
        Assert.Equal(new CornerRadius(8), metrics.WindowCornerRadius);
        Assert.Equal(new Thickness(14), metrics.ShadowMargin);
        Assert.Equal(420 + 28, metrics.Width);
        Assert.Equal(560 + 28, metrics.Height);
    }

    [Theory]
    [InlineData(true, AppBackdropStyle.Translucent, 8)]  // Win11 Translucent -> 8px
    [InlineData(true, AppBackdropStyle.Solid, 8)]        // Win11 Solid -> 8px
    [InlineData(false, AppBackdropStyle.Translucent, 14)]// macOS Translucent -> 14px
    [InlineData(false, AppBackdropStyle.Solid, 14)]      // macOS Solid -> 14px
    public void CalculateChromeMetrics_OnWindows11OrMac_AlwaysPreservesRoundedCorners(
        bool isWindows, AppBackdropStyle style, double expectedRadius)
    {
        var metrics = WindowsPlatformChromeHelper.CalculateChromeMetrics(
            isWindows: isWindows,
            isWin10: false,
            backdropStyle: style,
            baseWidth: 420,
            baseHeight: 560,
            minWidth: 340,
            minHeight: 400,
            maxWidth: 800,
            maxHeight: 1200);

        Assert.Equal(WindowDecorations.BorderOnly, metrics.Decorations);
        Assert.Equal(new CornerRadius(expectedRadius), metrics.WindowCornerRadius);
        Assert.Equal(new Thickness(0), metrics.ShadowMargin);
        Assert.Equal(420, metrics.Width);
        Assert.Equal(560, metrics.Height);

        if (style == AppBackdropStyle.Solid)
        {
            Assert.DoesNotContain(WindowTransparencyLevel.AcrylicBlur, metrics.TransparencyLevels);
        }
        else
        {
            Assert.Contains(WindowTransparencyLevel.AcrylicBlur, metrics.TransparencyLevels);
        }
    }

    [Fact]
    public async Task SettingsViewModel_BackdropStyle_PersistsAndRaisesEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Backdrop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsService = new SettingsService(null, tempDir);
            var history = new ClipboardHistory(Path.Combine(tempDir, "test.db"), Path.Combine(tempDir, "images"));
            await history.InitializeAsync();
            var loc = new LocalizationService("zh-CN");
            var vm = new SettingsViewModel(settingsService, history, null, loc);

            Assert.Equal("主题风格", loc["Settings_Appearance_Backdrop"]);
            Assert.Equal("通透", loc["Settings_Appearance_Translucent"]);
            Assert.Equal("实色", loc["Settings_Appearance_Solid"]);

            AppBackdropStyle? notifiedStyle = null;
            vm.BackdropStyleChanged += (_, s) => notifiedStyle = s;

            vm.BackdropStyle = AppBackdropStyle.Solid;

            Assert.Equal(AppBackdropStyle.Solid, notifiedStyle);
            Assert.Equal(AppBackdropStyle.Solid, settingsService.Settings.BackdropStyle);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Fluent2Win10OverrideTheme_LoadsSuccessfullyWithFourDimensionalSurfaceTokens()
    {
        var uri = new Uri("avares://SlimClip.UI/Themes/Fluent2Win10Override.axaml");
        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

        Assert.NotNull(dict);
        Assert.True(dict.TryGetResource("WindowShadowMargin", null, out var marginObj));
        Assert.Equal(new Thickness(14), marginObj);

        Assert.Equal(3, dict.ThemeDictionaries.Count); // Default, Dark, Light

        foreach (var themeVariant in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
        {
            Assert.True(dict.TryGetResource("WindowElevationShadow", themeVariant, out var shadowObj));
            var shadows = Assert.IsType<BoxShadows>(shadowObj);
            Assert.True(shadows.Count >= 2);

            Assert.True(dict.TryGetResource("SurfaceBackground", themeVariant, out var surfaceObj));
            Assert.IsAssignableFrom<IGradientBrush>(surfaceObj);

            Assert.True(dict.TryGetResource("WindowInnerRimBrush", themeVariant, out var innerRimObj));
            Assert.IsAssignableFrom<IBrush>(innerRimObj);

            Assert.True(dict.TryGetResource("CardBackground", themeVariant, out var cardBgObj));
            Assert.IsAssignableFrom<IBrush>(cardBgObj);

            Assert.True(dict.TryGetResource("SearchBackground", themeVariant, out var searchBgObj));
            Assert.IsAssignableFrom<IBrush>(searchBgObj);
        }
    }
}
