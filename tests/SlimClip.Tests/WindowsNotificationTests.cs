using SlimClip.Platform.Windows;
using Xunit;

namespace SlimClip.Tests;

public class WindowsNotificationTests
{
    [Fact]
    public void BuildToastXml_EscapesXmlSpecialCharactersAndIncludesLogoWhenProvided()
    {
        var xml = WindowsNotificationService.BuildToastXml(
            "Title <A&B>",
            "Version \"v1.0.8\" ready > click",
            @"C:\Apps\SlimClip\Assets\app&logo.png");

        Assert.Contains("Title &lt;A&amp;B&gt;", xml);
        Assert.Contains("Version &quot;v1.0.8&quot; ready &gt; click", xml);
        Assert.Contains("appLogoOverride", xml);
        Assert.Contains("app&amp;logo.png", xml);
    }

    [Fact]
    public void BuildToastXml_OmitsLogoElementWhenPathEmpty()
    {
        var xml = WindowsNotificationService.BuildToastXml("Ready", "Body", null);

        Assert.DoesNotContain("appLogoOverride", xml);
        Assert.Contains("<text>Ready</text>", xml);
        Assert.Contains("<text>Body</text>", xml);
    }
}
