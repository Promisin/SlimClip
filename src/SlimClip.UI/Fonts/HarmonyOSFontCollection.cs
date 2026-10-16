using System;
using Avalonia;
using Avalonia.Media.Fonts;

using Avalonia.Platform;

namespace SlimClip.UI.Fonts;

public sealed class HarmonyOSFontCollection : EmbeddedFontCollection
{
    private static readonly Uri KeyUri = new("fonts:HarmonyOS", UriKind.Absolute);
    private static readonly Uri SourceUri = new("avares://SlimClip.UI/Assets/Fonts", UriKind.Absolute);

    public HarmonyOSFontCollection() : base(KeyUri, SourceUri)
    {
    }
}

public static class FontAppBuilderExtensions
{
    public static AppBuilder WithHarmonyOSFont(this AppBuilder builder)
    {
        return builder.ConfigureFonts(manager => manager.AddFontCollection(new HarmonyOSFontCollection()));
    }
}
