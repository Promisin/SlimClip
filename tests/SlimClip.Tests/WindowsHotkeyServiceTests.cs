using System;
using SlimClip.Core.Models;
using SlimClip.Platform.Windows;
using Xunit;

namespace SlimClip.Tests;

public class WindowsHotkeyServiceTests
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_V = 86;

    [Fact]
    public void WinV_WhenTriggered_SwallowsVDownAndUp_AndNeutralizesWinKeyUp()
    {
        using var service = new WindowsHotkeyService();
        var hotkeyFired = false;
        var neutralizedCount = 0;

        service.HotkeyPressed += (s, e) => hotkeyFired = true;
        service.OnWinKeyNeutralized = () => neutralizedCount++;

        service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        // 1. Physical Win Down
        var resWinDown = service.ProcessKeyEvent(WM_KEYDOWN, VK_LWIN, 0, isInjected: false);
        Assert.Equal(IntPtr.Zero, resWinDown);
        Assert.False(hotkeyFired);

        // 2. Physical V Down (HotKey Match!)
        var resVDown = service.ProcessKeyEvent(WM_KEYDOWN, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resVDown); // Must swallow 'V' down
        Assert.True(hotkeyFired);
        Assert.True(service.IsWinKeySuppressionActive, "Win key suppression must be active while Win is held");
        Assert.Equal(VK_V, service.SuppressedSuffixVk);
        Assert.Equal(1, neutralizedCount); // Neutralized on chord trigger

        // 3. Physical V Up
        var resVUp = service.ProcessKeyEvent(WM_KEYUP, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resVUp); // Must swallow 'V' up so background app doesn't receive orphan release
        Assert.Equal(0u, service.SuppressedSuffixVk);

        // 4. Physical Win Up (User releases Win key)
        var resWinUp = service.ProcessKeyEvent(WM_KEYUP, VK_LWIN, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resWinUp); // Must swallow Win keyup so Windows Shell never receives orphan Win release
        Assert.Equal(2, neutralizedCount); // Must neutralize Win key again upon release to suppress Start Menu
        Assert.False(service.IsWinKeySuppressionActive, "Win key suppression must be reset after Win keyup");
    }

    [Fact]
    public void WinAlone_DoesNotNeutralizeWinKeyUp_AllowsNativeStartMenu()
    {
        using var service = new WindowsHotkeyService();
        var neutralizedCount = 0;
        service.OnWinKeyNeutralized = () => neutralizedCount++;

        service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        // User taps Win key alone (normal Start menu request)
        service.ProcessKeyEvent(WM_KEYDOWN, VK_LWIN, 0, isInjected: false);
        var resWinUp = service.ProcessKeyEvent(WM_KEYUP, VK_LWIN, 0, isInjected: false);

        Assert.Equal(IntPtr.Zero, resWinUp);
        Assert.Equal(0, neutralizedCount);
        Assert.False(service.IsWinKeySuppressionActive);
    }

    [Fact]
    public void WinV_WhenWinReleasedBeforeV_BothKeysNeutralizedProperly()
    {
        using var service = new WindowsHotkeyService();
        var hotkeyFired = false;
        var neutralizedCount = 0;

        service.HotkeyPressed += (s, e) => hotkeyFired = true;
        service.OnWinKeyNeutralized = () => neutralizedCount++;

        service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        // 1. Win Down
        service.ProcessKeyEvent(WM_KEYDOWN, VK_LWIN, 0, isInjected: false);

        // 2. V Down
        var resVDown = service.ProcessKeyEvent(WM_KEYDOWN, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resVDown);
        Assert.True(hotkeyFired);

        // 3. User releases Win BEFORE releasing V (rolling fingers)
        var resWinUp = service.ProcessKeyEvent(WM_KEYUP, VK_LWIN, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resWinUp); // Must swallow Win keyup even if released before V!
        Assert.Equal(2, neutralizedCount); // Start menu must be suppressed on release!
        Assert.False(service.IsWinKeySuppressionActive);

        // 4. User releases V
        var resVUp = service.ProcessKeyEvent(WM_KEYUP, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resVUp); // Must still swallow V up!
    }

    [Fact]
    public void WinV_WhenHeldDown_DoesNotRepeatedlyTriggerHotkeyPressed_SwallowsAllRepeats()
    {
        using var service = new WindowsHotkeyService();
        var hotkeyFireCount = 0;

        service.HotkeyPressed += (s, e) => hotkeyFireCount++;

        service.Register(new HotkeyConfig
        {
            Modifiers = KeyModifiers.WindowsOrCommand,
            KeyCode = (int)VK_V,
            DisplayString = "Win + V",
            IsEnabled = true
        });

        // 1. Win Down
        service.ProcessKeyEvent(WM_KEYDOWN, VK_LWIN, 0, isInjected: false);

        // 2. V Down - 1st trigger
        var resV1 = service.ProcessKeyEvent(WM_KEYDOWN, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resV1);
        Assert.Equal(1, hotkeyFireCount);

        // 3. V Down repeated (hardware auto-repeat) - 2nd KeyDown
        var resV2 = service.ProcessKeyEvent(WM_KEYDOWN, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resV2); // Must swallow repeat
        Assert.Equal(1, hotkeyFireCount); // Must NOT fire again to prevent Dismiss toggle jitter!

        // 4. V Down repeated (hardware auto-repeat) - 3rd KeyDown
        var resV3 = service.ProcessKeyEvent(WM_KEYDOWN, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resV3);
        Assert.Equal(1, hotkeyFireCount);

        // 5. V Up
        var resVUp = service.ProcessKeyEvent(WM_KEYUP, VK_V, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resVUp);

        // 6. Win Up
        var resWinUp = service.ProcessKeyEvent(WM_KEYUP, VK_LWIN, 0, isInjected: false);
        Assert.Equal((IntPtr)1, resWinUp);
    }
}
