using System;
using Avalonia.Platform;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class TrayIconResourceTests
{
    [Fact]
    public void AssetLoader_ShouldFindTrayIcon_InCorrectAssembly()
    {
        StyleTests.EnsureHeadlessApp();

        var oldTrayUri = new Uri("avares://SlimClip.Desktop/Assets/icon_tray.ico");
        var oldAppUri = new Uri("avares://SlimClip.Desktop/Assets/app.ico");

        var correctTrayUri = new Uri("avares://SlimClip/Assets/icon_tray.ico");
        var correctAppUri = new Uri("avares://SlimClip/Assets/app.ico");

        // Old URIs targeting SlimClip.Desktop fail:
        Assert.False(AssetLoader.Exists(oldTrayUri), "avares://SlimClip.Desktop/Assets/icon_tray.ico must not exist because assembly name is SlimClip");
        Assert.False(AssetLoader.Exists(oldAppUri), "avares://SlimClip.Desktop/Assets/app.ico must not exist because assembly name is SlimClip");

        // Correct URIs targeting SlimClip must exist:
        Assert.True(AssetLoader.Exists(correctTrayUri), "avares://SlimClip/Assets/icon_tray.ico must exist");
        Assert.True(AssetLoader.Exists(correctAppUri), "avares://SlimClip/Assets/app.ico must exist");
    }

    [Fact]
    public void App_LoadTrayIcon_Returns_NonNull_WindowIcon()
    {
        StyleTests.EnsureHeadlessApp();

        var icon = SlimClip.Desktop.App.LoadTrayIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void App_LoadAppIcon_Returns_NonNull_WindowIcon()
    {
        StyleTests.EnsureHeadlessApp();

        var icon = SlimClip.Desktop.App.LoadAppIcon();
        Assert.NotNull(icon);
    }
}
