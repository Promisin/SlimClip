using Avalonia.Input;
using SlimClip.UI.Helpers;
using Xunit;

namespace SlimClip.Tests;

public class KeyGestureHelperTests
{
    [Theory]
    [InlineData("⇧⌘V", Key.V, KeyModifiers.Shift | KeyModifiers.Meta)]
    [InlineData("Alt + Shift + V", Key.V, KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData("Ctrl+C", Key.C, KeyModifiers.Control)]
    [InlineData("Ctrl + E", Key.E, KeyModifiers.Control)]
    [InlineData("Ctrl + P", Key.P, KeyModifiers.Control)]
    [InlineData("⌃⌥⇧⌘K", Key.K, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)]
    [InlineData("F12", Key.F12, KeyModifiers.None)]
    [InlineData("F5", Key.F5, KeyModifiers.None)]
    public void TryParse_ValidInputs_ReturnsExpectedKeyGesture(string input, Key expectedKey, KeyModifiers expectedModifiers)
    {
        var gesture = KeyGestureHelper.TryParse(input);

        Assert.NotNull(gesture);
        Assert.Equal(expectedKey, gesture!.Key);
        Assert.Equal(expectedModifiers, gesture.KeyModifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("无")]
    [InlineData("Invalid!@#$")]
    public void TryParse_InvalidOrEmptyInputs_ReturnsNull(string? input)
    {
        var gesture = KeyGestureHelper.TryParse(input);
        Assert.Null(gesture);
    }
}
