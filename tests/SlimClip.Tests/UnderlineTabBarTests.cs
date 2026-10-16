using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SlimClip.UI.Controls;
using Xunit;

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class UnderlineTabBarTests
{
    public UnderlineTabBarTests()
    {
        StyleTests.EnsureHeadlessApp();
    }

    [Fact]
    public void UnderlineTabBar_And_Items_Must_Be_Focus_Immune()
    {
        var bar = new UnderlineTabBar();
        var item1 = new UnderlineTabItem { Content = "全部", Value = "All" };
        var item2 = new UnderlineTabItem { Content = "文本", Value = "Text" };
        bar.Items.Add(item1);
        bar.Items.Add(item2);

        Assert.False(bar.Focusable, "UnderlineTabBar must have Focusable=false to prevent stealing search box focus");
        Assert.False(item1.Focusable, "UnderlineTabItem must have Focusable=false to prevent stealing search box focus");
        Assert.False(item2.Focusable, "UnderlineTabItem must have Focusable=false to prevent stealing search box focus");
    }

    [Fact]
    public void UnderlineTabBar_Selection_Syncs_With_SelectedValue_And_SelectedIndex()
    {
        var bar = new UnderlineTabBar();
        var item1 = new UnderlineTabItem { Content = "收藏", Value = "Favorite" };
        var item2 = new UnderlineTabItem { Content = "全部", Value = "All" };
        var item3 = new UnderlineTabItem { Content = "文本", Value = "Text" };
        bar.Items.Add(item1);
        bar.Items.Add(item2);
        bar.Items.Add(item3);

        var window = new Window { Content = bar };
        window.Show();
        window.UpdateLayout();

        try
        {
            // Initial selection defaults or syncs
            bar.SelectedValue = "All";
            window.UpdateLayout();

            Assert.Equal("All", bar.SelectedValue);
            Assert.Equal(1, bar.SelectedIndex);
            Assert.False(item1.IsSelected);
            Assert.True(item2.IsSelected);
            Assert.False(item3.IsSelected);

            // Change selected value
            bar.SelectedValue = "Text";
            window.UpdateLayout();

            Assert.Equal("Text", bar.SelectedValue);
            Assert.Equal(2, bar.SelectedIndex);
            Assert.False(item2.IsSelected);
            Assert.True(item3.IsSelected);

            // Change selected index
            bar.SelectedIndex = 0;
            window.UpdateLayout();

            Assert.Equal("Favorite", bar.SelectedValue);
            Assert.Equal(0, bar.SelectedIndex);
            Assert.True(item1.IsSelected);
            Assert.False(item3.IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void UnderlineTabBar_Cycle_Navigates_Forward_And_Backward_Properly()
    {
        var bar = new UnderlineTabBar();
        bar.Items.Add(new UnderlineTabItem { Content = "收藏", Value = "Favorite" });
        bar.Items.Add(new UnderlineTabItem { Content = "全部", Value = "All" });
        bar.Items.Add(new UnderlineTabItem { Content = "文本", Value = "Text" });

        var window = new Window { Content = bar };
        window.Show();
        window.UpdateLayout();

        try
        {
            bar.SelectedValue = "All"; // Index 1
            Assert.Equal(1, bar.SelectedIndex);

            // Cycle forward -> Text (Index 2)
            bar.Cycle(forward: true);
            Assert.Equal(2, bar.SelectedIndex);
            Assert.Equal("Text", bar.SelectedValue);

            // Cycle forward from end -> stops at Text (Index 2), does NOT loop
            bar.Cycle(forward: true);
            Assert.Equal(2, bar.SelectedIndex);
            Assert.Equal("Text", bar.SelectedValue);

            // Cycle backward -> All (Index 1)
            bar.Cycle(forward: false);
            Assert.Equal(1, bar.SelectedIndex);
            Assert.Equal("All", bar.SelectedValue);

            // Cycle backward -> Favorite (Index 0)
            bar.Cycle(forward: false);
            Assert.Equal(0, bar.SelectedIndex);
            Assert.Equal("Favorite", bar.SelectedValue);

            // Cycle backward from 0 -> stops at Favorite (Index 0), does NOT loop
            bar.Cycle(forward: false);
            Assert.Equal(0, bar.SelectedIndex);
            Assert.Equal("Favorite", bar.SelectedValue);

            // If wrapAround is explicitly enabled, it wraps
            bar.Cycle(forward: false, wrapAround: true);
            Assert.Equal(2, bar.SelectedIndex);
            Assert.Equal("Text", bar.SelectedValue);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void UnderlineTabItem_FontWeight_Remains_Constant_SemiBold_Across_States()
    {
        var item = new UnderlineTabItem { Content = "全部", Value = "All" };
        var window = new Window { Content = item };
        window.Show();
        window.UpdateLayout();

        try
        {
            // 1. Unselected state
            item.IsSelected = false;
            item.UpdateVisualState();
            Assert.Equal(FontWeight.SemiBold, item.FontWeight);

            // 2. Selected state
            item.IsSelected = true;
            item.UpdateVisualState();
            Assert.Equal(FontWeight.SemiBold, item.FontWeight);

            // 3. Hover state
            item.UpdateVisualState();
            Assert.Equal(FontWeight.SemiBold, item.FontWeight);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void UnderlineTabItem_TryGetIndicatorBounds_Hugs_Text_With_Margin()
    {
        var bar = new UnderlineTabBar();
        var item = new UnderlineTabItem { Content = "全部", Value = "All" };
        bar.Items.Add(item);

        var window = new Window { Content = bar };
        window.Show();
        window.UpdateLayout();

        try
        {
            bool success = item.TryGetIndicatorBounds(bar, out double x, out double width);
            Assert.True(success, "TryGetIndicatorBounds should succeed");
            Assert.True(width > 0, "Indicator width should be positive");

            // Text "全部" with font size 13 is roughly 26px wide, plus 8px (+4px on each side) is approx 34px.
            // In all cases, indicator width must be smaller than the whole tab item width including padding.
            Assert.True(width <= item.Bounds.Width, $"Indicator width {width} should hug text and be within item width {item.Bounds.Width}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void UnderlineTabItem_PointerOver_Shows_Hover_Border_Shadow()
    {
        var item = new UnderlineTabItem { Content = "文本", Value = "Text" };
        var window = new Window { Content = item };
        window.Show();
        window.UpdateLayout();

        try
        {
            var hoverBorder = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_HoverBorder");
            Assert.NotNull(hoverBorder);

            // Verified rounded rectangle specifications: CornerRadius=6, soft ambient shadow
            Assert.Equal(new CornerRadius(6), hoverBorder.CornerRadius);
            Assert.True(hoverBorder.BoxShadow.Count > 0);
            var shadow = hoverBorder.BoxShadow[0];
            Assert.Equal(0, shadow.OffsetX);
            Assert.Equal(2, shadow.OffsetY);
            Assert.Equal(6, shadow.Blur);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_First_ShowPanel_Must_Display_Indicator_Immediately()
    {
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        try
        {
            // Simulate desktop app invoking ShowPanel() for the very first time
            panel.ShowPanel();
            panel.UpdateLayout();

            var tabBar = panel.FindControl<UnderlineTabBar>("FilterTabBar");
            Assert.NotNull(tabBar);

            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);

            Assert.True(indicator.IsVisible, "Indicator must be visible immediately on first ShowPanel");
            Assert.True(indicator.Width > 0, $"Indicator width must be > 0 on first ShowPanel, got {indicator.Width}");
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void SettingsWindow_Must_Use_UnderlineTabBar_And_Switch_Panels()
    {
        var window = new SlimClip.UI.Views.SettingsWindow();
        window.Show();
        window.UpdateLayout();

        try
        {
            var tabBar = window.FindControl<UnderlineTabBar>("SettingsTabBar");
            Assert.NotNull(tabBar);
            Assert.Equal("General", tabBar.SelectedValue);

            var panelGeneral = window.FindControl<ScrollViewer>("PanelGeneral")!;
            var panelAppearance = window.FindControl<ScrollViewer>("PanelAppearance")!;
            var panelHotkey = window.FindControl<ScrollViewer>("PanelHotkey")!;

            Assert.True(panelGeneral.IsVisible);
            Assert.False(panelAppearance.IsVisible);

            // Switch to Appearance
            tabBar.SelectedValue = "Appearance";
            window.UpdateLayout();

            Assert.False(panelGeneral.IsVisible);
            Assert.True(panelAppearance.IsVisible);

            // Switch to Hotkey
            tabBar.SelectedValue = "Hotkey";
            window.UpdateLayout();

            Assert.False(panelAppearance.IsVisible);
            Assert.True(panelHotkey.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SettingsWindow_InitialShow_Indicator_MustBe_Properly_Positioned()
    {
        var window = new SlimClip.UI.Views.SettingsWindow();
        window.Show();
        window.UpdateLayout();

        try
        {
            var tabBar = window.FindControl<UnderlineTabBar>("SettingsTabBar");
            Assert.NotNull(tabBar);

            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);

            var tabGeneral = window.FindControl<UnderlineTabItem>("TabGeneral")!;
            var tb = tabGeneral.GetVisualDescendants().OfType<TextBlock>().First();

            Assert.True(indicator.IsEffectivelyVisible, "Indicator must be effectively visible");
            Assert.True(indicator.Bounds.Width > 0, $"Indicator Bounds.Width must be > 0, got {indicator.Bounds.Width}");
            Assert.True(indicator.Bounds.Height > 0, $"Indicator Bounds.Height must be > 0, got {indicator.Bounds.Height}");

            var tbPt = tb.TranslatePoint(new Point(0, 0), tabBar);
            Assert.True(tbPt.HasValue);
            var expectedX = tbPt.Value.X - 4.0;
            var expectedW = tb.Bounds.Width + 8.0;

            Assert.InRange(indicator.Bounds.X, expectedX - 2.0, expectedX + 2.0);
            Assert.InRange(indicator.Bounds.Width, expectedW - 2.0, expectedW + 2.0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_First_ShowPanel_Indicator_MustBe_Aligned_With_All_Tab()
    {
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        try
        {
            panel.ShowPanel();
            panel.UpdateLayout();

            var tabBar = panel.FindControl<UnderlineTabBar>("FilterTabBar");
            Assert.NotNull(tabBar);

            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);

            Assert.True(indicator.IsEffectivelyVisible, "Indicator must be effectively visible");
            Assert.True(indicator.Bounds.Width > 0, $"Indicator Bounds.Width must be > 0, got {indicator.Bounds.Width}");
            Assert.True(indicator.Bounds.Height > 0, $"Indicator Bounds.Height must be > 0, got {indicator.Bounds.Height}");

            var tabs = tabBar.GetTabItems();
            var allTab = tabs.FirstOrDefault(t => t.Value == "All");
            Assert.NotNull(allTab);

            var tb = allTab.GetVisualDescendants().OfType<TextBlock>().First();
            var tbPt = tb.TranslatePoint(new Point(0, 0), tabBar);
            Assert.True(tbPt.HasValue);
            var expectedX = tbPt.Value.X - 4.0;
            var expectedW = tb.Bounds.Width + 8.0;

            Assert.InRange(indicator.Bounds.X, expectedX - 2.0, expectedX + 2.0);
            Assert.InRange(indicator.Bounds.Width, expectedW - 2.0, expectedW + 2.0);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_PreWarmed_Then_ShowPanel_CheckIndicator()
    {
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        try
        {
            // Simulate App.axaml.cs PreWarmClipboardPanel:
            panel.Opacity = 0;
            panel.Show();
            panel.Hide();
            panel.Opacity = 1;

            // Now simulate user hotkey pressing ShowPanel:
            panel.ShowPanel();
            panel.UpdateLayout();

            var tabBar = panel.FindControl<UnderlineTabBar>("FilterTabBar");
            Assert.NotNull(tabBar);

            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);

            Assert.True(indicator.IsEffectivelyVisible, "Indicator must be effectively visible");
            Assert.True(indicator.Bounds.Width > 0, $"Indicator Bounds.Width must be > 0, got {indicator.Bounds.Width}");
            Assert.True(indicator.Bounds.Height > 0, $"Indicator Bounds.Height must be > 0, got {indicator.Bounds.Height}");

            var tabs = tabBar.GetTabItems();
            var allTab = tabs.FirstOrDefault(t => t.Value == "All");
            Assert.NotNull(allTab);

            var tb = allTab.GetVisualDescendants().OfType<TextBlock>().First();
            var tbPt = tb.TranslatePoint(new Point(0, 0), tabBar);
            Assert.True(tbPt.HasValue);
            var expectedX = tbPt.Value.X - 4.0;
            var expectedW = tb.Bounds.Width + 8.0;

            Assert.InRange(indicator.Bounds.X, expectedX - 2.0, expectedX + 2.0);
            Assert.InRange(indicator.Bounds.Width, expectedW - 2.0, expectedW + 2.0);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_Check_All_Tab_Indicator_Before_And_After()
    {
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        try
        {
            panel.Opacity = 0;
            panel.Show();
            panel.Hide();
            panel.Opacity = 1;

            panel.ShowPanel();
            panel.UpdateLayout();

            var tabBar = panel.FindControl<UnderlineTabBar>("FilterTabBar")!;
            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator")!;
            
            Assert.True(indicator.IsVisible, "Indicator must be visible after ShowPanel");
            Assert.True(indicator.Bounds.Width > 0, "Indicator bounds width must be > 0");
            Assert.Equal(3.0, indicator.Bounds.Height);

            var allTab = tabBar.GetTabItems().FirstOrDefault(t => t.Value == "All")!;
            var tb = allTab.GetVisualDescendants().OfType<TextBlock>().First();
            var tbPt = tb.TranslatePoint(new Point(0, 0), tabBar);
            Assert.True(tbPt.HasValue);

            var expectedX = tbPt.Value.X - 4.0;
            var expectedW = tb.Bounds.Width + 8.0;

            Assert.InRange(indicator.Bounds.X, expectedX - 2.0, expectedX + 2.0);
            Assert.InRange(indicator.Bounds.Width, expectedW - 2.0, expectedW + 2.0);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_Filter_HorizontalNavigation_DoesNotLoop()
    {
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        try
        {
            panel.ShowPanel();
            panel.UpdateLayout();

            var tabBar = panel.FindControl<UnderlineTabBar>("FilterTabBar")!;
            Assert.Equal("All", tabBar.SelectedValue); // Initial: All (Index 1)

            // Navigate left to Favorite (Index 0)
            tabBar.Cycle(forward: false);
            Assert.Equal("Favorite", tabBar.SelectedValue);
            Assert.Equal(0, tabBar.SelectedIndex);

            // Navigate left from start -> stays at Favorite (Index 0), does NOT loop to File
            tabBar.Cycle(forward: false);
            Assert.Equal("Favorite", tabBar.SelectedValue);
            Assert.Equal(0, tabBar.SelectedIndex);

            // Navigate right all the way to File (Index 4)
            tabBar.Cycle(forward: true); // All
            tabBar.Cycle(forward: true); // Text
            tabBar.Cycle(forward: true); // Image
            tabBar.Cycle(forward: true); // File
            Assert.Equal("File", tabBar.SelectedValue);
            Assert.Equal(4, tabBar.SelectedIndex);

            // Navigate right from end -> stays at File (Index 4), does NOT loop to Favorite
            tabBar.Cycle(forward: true);
            Assert.Equal("File", tabBar.SelectedValue);
            Assert.Equal(4, tabBar.SelectedIndex);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void UnderlineTabItem_DefaultBackground_MustBe_Transparent_ToSupportHitTesting()
    {
        var item = new UnderlineTabItem { Content = "全部", Value = "All" };
        Assert.Equal(Brushes.Transparent, item.Background);
    }

    [Fact]
    public void UnderlineTabBar_DefaultBackground_MustBe_Transparent_ToSupportHitTesting()
    {
        var bar = new UnderlineTabBar();
        Assert.Equal(Brushes.Transparent, bar.Background);
    }

    [Fact]
    public void UnderlineTabBar_Click_OnItem_Or_ProximityGap_SelectsTab()
    {
        var bar = new UnderlineTabBar();
        var item1 = new UnderlineTabItem { Content = "收藏", Value = "Favorite" };
        var item2 = new UnderlineTabItem { Content = "全部", Value = "All" };
        var item3 = new UnderlineTabItem { Content = "文本", Value = "Text" };
        bar.Items.Add(item1);
        bar.Items.Add(item2);
        bar.Items.Add(item3);

        var window = new Window { Content = bar, Width = 400, Height = 100 };
        window.Show();
        window.UpdateLayout();

        try
        {
            Assert.Equal("Favorite", bar.SelectedValue);

            var pointer = new Pointer(0, PointerType.Mouse, true);

            // 1. Direct item click: PointerPressed on item2
            var item2Event = new PointerPressedEventArgs(
                item2,
                pointer,
                item2,
                new Point(item2.Bounds.Width / 2.0, item2.Bounds.Height / 2.0),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None);
            item2.RaiseEvent(item2Event);

            Assert.Equal("All", bar.SelectedValue);
            Assert.Equal(1, bar.SelectedIndex);
            Assert.True(item2Event.Handled);

            // 2. Proximity gap click: PointerPressed on bar at coordinate closer to item3
            var item3Center = item3.Bounds.X + item3.Bounds.Width / 2.0;
            var gapEvent = new PointerPressedEventArgs(
                bar,
                pointer,
                bar,
                new Point(item3Center - 2.0, item3.Bounds.Height / 2.0),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None);
            bar.RaiseEvent(gapEvent);

            Assert.Equal("Text", bar.SelectedValue);
            Assert.Equal(2, bar.SelectedIndex);
            Assert.True(gapEvent.Handled);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SettingsWindow_AllPanels_FirstCard_TopDistance_MustBe_Identical()
    {
        var window = new SlimClip.UI.Views.SettingsWindow();
        window.Show();
        window.UpdateLayout();

        try
        {
            var panels = new[]
            {
                window.FindControl<ScrollViewer>("PanelGeneral")!,
                window.FindControl<ScrollViewer>("PanelAppearance")!,
                window.FindControl<ScrollViewer>("PanelHotkey")!,
                window.FindControl<ScrollViewer>("PanelData")!,
                window.FindControl<ScrollViewer>("PanelAdvanced")!,
                window.FindControl<ScrollViewer>("PanelAbout")!
            };

            double? referenceTop = null;

            foreach (var panel in panels)
            {
                panel.IsVisible = true;
                window.UpdateLayout();

                var stackPanel = panel.GetVisualDescendants().OfType<StackPanel>().First();
                var firstCard = stackPanel.Children.First();
                var pt = firstCard.TranslatePoint(new Point(0, 0), window);
                Assert.True(pt.HasValue);

                if (referenceTop == null)
                {
                    referenceTop = pt.Value.Y;
                }
                else
                {
                    Assert.True(Math.Abs(pt.Value.Y - referenceTop.Value) < 0.5,
                        $"Panel {panel.Name} first card Y ({pt.Value.Y}) must match reference Y ({referenceTop.Value})");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }
}
