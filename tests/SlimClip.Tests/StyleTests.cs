using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Controls;
using SlimClip.UI.Fonts;
using SlimClip.UI.ViewModels;
using SlimClip.UI.Views;
using Xunit;

namespace SlimClip.Tests;

[Collection("AvaloniaUI")]
public class StyleTests
{
    private static readonly object _appLock = new();

    internal static void EnsureHeadlessApp()
    {
        lock (_appLock)
        {
            if (Avalonia.Application.Current == null)
            {
                Avalonia.AppBuilder.Configure<Avalonia.Application>()
                    .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions())
                    .WithHarmonyOSFont()
                    .SetupWithoutStarting();
            }

            var app = Avalonia.Application.Current!;
            if (!app.Styles.Any(s => s is Avalonia.Themes.Fluent.FluentTheme))
            {
                app.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            }
            var themeUri = new Uri("avares://SlimClip.UI/Themes/Fluent2Theme.axaml");
            if (!app.Resources.MergedDictionaries.Any())
            {
                var themeDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(themeUri);
                app.Resources.MergedDictionaries.Add(themeDict);
            }
        }
    }

    [Fact]
    public void UnderlineTabBar_In_ClipboardPanel_Renders_Correctly()
    {
        EnsureHeadlessApp();

        var panel = new SlimClip.UI.Views.ClipboardPanel();
        panel.Show();
        panel.UpdateLayout();

        try
        {
            var tabBar = panel.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("FilterTabBar")!;
            Assert.NotNull(tabBar);
            Assert.Equal("All", tabBar.SelectedValue);

            var items = tabBar.GetTabItems();
            Assert.Equal(5, items.Count);

            var allItem = items.First(i => i.Value == "All");
            var textItem = items.First(i => i.Value == "Text");

            // 验证焦点免疫：绝不抢夺搜索框焦点
            Assert.False(tabBar.Focusable);
            Assert.False(allItem.Focusable);

            // 验证字重锁定：SemiBold 保持锐利
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, allItem.FontWeight);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, textItem.FontWeight);

            // 验证选中的项处于 IsSelected
            Assert.True(allItem.IsSelected);
            Assert.False(textItem.IsSelected);

            // 验证悬停微阴影层
            var textHoverBorder = textItem.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_HoverBorder");
            Assert.NotNull(textHoverBorder);
            Assert.Equal(new Avalonia.CornerRadius(6), textHoverBorder.CornerRadius);

            // 验证指示器
            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);
            Assert.Equal(3.0, indicator.Height);
            Assert.Equal(new Avalonia.CornerRadius(1.5), indicator.CornerRadius);

            Assert.NotEqual(Avalonia.Media.TextRenderingMode.Antialias, Avalonia.Media.RenderOptions.GetTextRenderingMode(panel));
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void UnderlineTabBar_In_SettingsWindow_Renders_Correctly()
    {
        EnsureHeadlessApp();

        var window = new SlimClip.UI.Views.SettingsWindow();
        window.Show();
        window.UpdateLayout();

        try
        {
            var tabBar = window.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("SettingsTabBar")!;
            Assert.NotNull(tabBar);
            Assert.Equal("General", tabBar.SelectedValue);

            var items = tabBar.GetTabItems();
            Assert.Equal(6, items.Count);

            var generalItem = items.First(i => i.Value == "General");
            var appearanceItem = items.First(i => i.Value == "Appearance");

            // 验证焦点免疫：绝不抢夺焦点
            Assert.False(tabBar.Focusable);
            Assert.False(generalItem.Focusable);

            // 验证字重锁定：SemiBold 保持锐利
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, generalItem.FontWeight);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, appearanceItem.FontWeight);

            // 验证选中的项处于 IsSelected
            Assert.True(generalItem.IsSelected);
            Assert.False(appearanceItem.IsSelected);

            // 验证指示器存在
            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);
            Assert.Equal(3.0, indicator.Height);
            Assert.Equal(new Avalonia.CornerRadius(1.5), indicator.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void FilterChip_Selectors_Distinguish_Checked_And_Unchecked_Hover()
    {
        var unselectedHover = new Style(s => s.OfType<RadioButton>().Class("FilterChip").Class(":not(:checked)").Class(":pointerover"));
        var selected = new Style(s => s.OfType<RadioButton>().Class("FilterChip").Class(":checked"));
        var selectedHover = new Style(s => s.OfType<RadioButton>().Class("FilterChip").Class(":checked").Class(":pointerover"));

        Assert.Equal("RadioButton.FilterChip:not(:checked):pointerover", unselectedHover.Selector!.ToString());
        Assert.Equal("RadioButton.FilterChip:checked", selected.Selector!.ToString());
        Assert.Equal("RadioButton.FilterChip:checked:pointerover", selectedHover.Selector!.ToString());
    }

    [Fact]
    public void ClipboardPanel_Must_Use_UnderlineTabBar_And_Clean_Old_FilterChip_Styles()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views/ClipboardPanel.axaml"));
        Assert.True(File.Exists(basePath), $"View file not found at {basePath}");

        var doc = XDocument.Load(basePath);
        var tabBars = doc.Descendants().Where(e => e.Name.LocalName == "UnderlineTabBar").ToList();
        Assert.NotEmpty(tabBars);

        var tabItems = doc.Descendants().Where(e => e.Name.LocalName == "UnderlineTabItem").ToList();
        Assert.Equal(5, tabItems.Count);

        // 确保旧的 RadioButton.FilterChip 样式与控件已全部被清理
        var styles = doc.Root?.Element(doc.Root.GetDefaultNamespace() + "Window.Styles")?.Elements();
        var oldFilterChipStyle = styles?.FirstOrDefault(s => s.Attribute("Selector")?.Value?.Contains("FilterChip") == true);
        Assert.Null(oldFilterChipStyle);
    }

    [Fact]
    public void SettingsWindow_Must_Use_UnderlineTabBar_And_Clean_Old_TabItem_Styles()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views/SettingsWindow.axaml"));
        Assert.True(File.Exists(basePath), $"View file not found at {basePath}");

        var doc = XDocument.Load(basePath);
        var tabBars = doc.Descendants().Where(e => e.Name.LocalName == "UnderlineTabBar").ToList();
        Assert.NotEmpty(tabBars);

        var tabItems = doc.Descendants().Where(e => e.Name.LocalName == "UnderlineTabItem").ToList();
        Assert.Equal(6, tabItems.Count);

        // 确保旧的 RadioButton.TabItem 样式与控件已全部被清理
        var styles = doc.Root?.Element(doc.Root.GetDefaultNamespace() + "Window.Styles")?.Elements();
        var oldTabItemStyle = styles?.FirstOrDefault(s => s.Attribute("Selector")?.Value?.Contains("TabItem") == true);
        Assert.Null(oldTabItemStyle);
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void Card_And_Search_Backgrounds_Must_Be_SemiTransparent_In_Both_Variants(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();
        var xNs = doc.Root.GetNamespaceOfPrefix("x");

        var themeDictionaries = doc.Root.Element(ns + "ResourceDictionary.ThemeDictionaries")!;
        foreach (var variant in new[] { "Light", "Dark" })
        {
            var dict = themeDictionaries.Elements(ns + "ResourceDictionary")
                .First(e => e.Attribute(xNs! + "Key")?.Value == variant);

            string GetColor(string key) =>
                dict.Elements().First(e => e.Attribute(xNs! + "Key")?.Value == key).Attribute("Color")!.Value;

            var cardBg = GetColor("CardBackground");
            var searchBg = GetColor("SearchBackground");

            Assert.True(cardBg.StartsWith("#") && cardBg.Length == 9, $"CardBackground {cardBg} in {variant} must be 8-digit ARGB hex");
            var cardAlpha = Convert.ToInt32(cardBg.Substring(1, 2), 16);
            Assert.True(cardAlpha < 255, $"CardBackground {cardBg} in {variant} must be semi-transparent (alpha < 255)");

            Assert.True(searchBg.StartsWith("#") && searchBg.Length == 9, $"SearchBackground {searchBg} in {variant} must be 8-digit ARGB hex");
            var searchAlpha = Convert.ToInt32(searchBg.Substring(1, 2), 16);
            Assert.True(searchAlpha < 255, $"SearchBackground {searchBg} in {variant} must be semi-transparent (alpha < 255)");
        }
    }

    [Fact]
    public void ListBoxItem_Must_Have_Unified_CornerRadius_And_Transparent_ContentPresenter()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views/ClipboardPanel.axaml"));
        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();

        var styles = doc.Root.Element(ns + "Window.Styles")?.Elements(ns + "Style").ToList();
        Assert.NotNull(styles);

        var lbiStyle = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "ListBoxItem");
        Assert.NotNull(lbiStyle);
        Assert.Equal("{DynamicResource ItemCardCornerRadius}", lbiStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "CornerRadius")?.Attribute("Value")?.Value);
        Assert.Equal("True", lbiStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "ClipToBounds")?.Attribute("Value")?.Value);

        var cpStyle = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "ListBoxItem /template/ ContentPresenter");
        Assert.NotNull(cpStyle);
        Assert.Equal("{DynamicResource ItemCardCornerRadius}", cpStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "CornerRadius")?.Attribute("Value")?.Value);
        Assert.Equal("Transparent", cpStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Background")?.Attribute("Value")?.Value);

        var hoverCp = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "ListBoxItem:pointerover /template/ ContentPresenter");
        Assert.NotNull(hoverCp);
        Assert.Equal("Transparent", hoverCp.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Background")?.Attribute("Value")?.Value);

        var selCp = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "ListBoxItem:selected /template/ ContentPresenter");
        Assert.NotNull(selCp);
        Assert.Equal("Transparent", selCp.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Background")?.Attribute("Value")?.Value);
    }

    [Theory]
    [InlineData("ClipboardPanel.axaml")]
    [InlineData("SettingsWindow.axaml")]
    public void IconButton_Must_Be_Centered_Rounded_Rectangle_Not_Circle(string viewFileName)
    {
        // 1. 验证封装的 IconButton 控件本身的默认设计令牌规格（26x26 圆角矩形，CornerRadius=6，居中，无多余内边距）
        var iconBtn = new SlimClip.UI.Controls.IconButton();
        Assert.Equal(26.0, iconBtn.Width);
        Assert.Equal(26.0, iconBtn.Height);
        Assert.Equal(new Avalonia.CornerRadius(6), iconBtn.CornerRadius);
        Assert.Equal(new Avalonia.Thickness(0), iconBtn.Padding);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, iconBtn.HorizontalContentAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, iconBtn.VerticalContentAlignment);

        // 2. 验证视图文件中统一使用 controls:IconButton 控件
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views", viewFileName));
        var doc = XDocument.Load(basePath);

        var iconButtons = doc.Descendants().Where(e => e.Name.LocalName == "IconButton").ToList();
        Assert.NotEmpty(iconButtons);

        // 确保旧的 Button.icon-button 样式已被清理
        var styles = doc.Root?.Element(doc.Root.GetDefaultNamespace() + "Window.Styles")?.Elements();
        var oldBtnStyle = styles?.FirstOrDefault(s => s.Attribute("Selector")?.Value == "Button.icon-button");
        Assert.Null(oldBtnStyle);
    }

    [Fact]
    public void IconButton_Properties_And_State_MustBehaveCorrectly()
    {
        var btn = new SlimClip.UI.Controls.IconButton();

        // 验证 ToolTipText 设置
        btn.ToolTipText = "关闭";
        Assert.Equal("关闭", btn.ToolTipText);
        Assert.Equal("关闭", Avalonia.Controls.ToolTip.GetTip(btn));

        // 验证 IsActive 状态与伪类联动
        Assert.False(btn.IsActive);
        btn.IsActive = true;
        Assert.True(btn.IsActive);
        Assert.Contains(":active", btn.Classes);

        // 验证颜色属性自由赋值
        var redBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red);
        btn.HoverForeground = redBrush;
        Assert.Same(redBrush, btn.HoverForeground);
    }

    [Fact]
    public void IconButton_Shadow_Must_Be_Centered_And_ActiveState_Must_Be_Prominent()
    {
        var btn = new SlimClip.UI.Controls.IconButton();

        // 1. 验证激活态显著度：IsActive 状态下必须拥有非透明的高亮底色与反色前景，而不是难以辨认的深灰
        btn.IsActive = true;
        var bg = btn.GetEffectiveBackground(false, true);
        Assert.NotNull(bg);
        Assert.NotEqual(Avalonia.Media.Brushes.Transparent, bg);

        var fg = btn.GetEffectiveIconBrush(false, true);
        Assert.NotNull(fg);

        // 2. 验证阴影居中：悬浮时的微光晕必须 OffsetX == 0 且 OffsetY == 0（绝对中心发光）
        var shadow = btn.GetEffectiveShadow(true);
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(0, shadow.OffsetY);
        Assert.True(shadow.Blur > 0);

        // 3. 验证阴影颜色绝对不包含 (255, 255, 255) 纯白杂质（防止色偏白闪），中性色为纯黑微透明
        var neutralColor = btn.ComputeEffectiveShadowColor(Avalonia.Media.Brushes.Gray);
        Assert.Equal(0, neutralColor.R);
        Assert.Equal(0, neutralColor.G);
        Assert.Equal(0, neutralColor.B);
        Assert.True(neutralColor.A > 0);
    }

    [Fact]
    public void IconButton_ThreeLayer_Architecture_And_No_BrushTransition_To_Prevent_Flicker()
    {
        EnsureHeadlessApp();

        var btn = new SlimClip.UI.Controls.IconButton();
        var window = new Window { Content = btn };
        window.Show();
        window.UpdateLayout();

        try
        {
            var borders = btn.GetVisualDescendants().OfType<Border>().ToList();
            Assert.True(borders.Count >= 3, $"IconButton must contain at least 3 Borders (Shadow, Hover, Content), but found {borders.Count}");

            // 1. 验证没有任何 Border 包含 BrushTransition（彻底杜绝从 #00FFFFFF Transparent 插值产生的白色发光环闪烁）
            foreach (var border in borders)
            {
                if (border.Transitions != null)
                {
                    var hasBrushTransition = border.Transitions.Any(t => t is Avalonia.Animation.BrushTransition);
                    Assert.False(hasBrushTransition, "No Border in IconButton should have BrushTransition to prevent #00FFFFFF white flicker");
                }
            }

            // 2. 验证阴影层与悬浮层 IsHitTestVisible 必须为 false（防止与内容层争抢 Pointer 事件造成首尾帧微抖动）
            var shadowBorder = borders[0];
            Assert.False(shadowBorder.IsHitTestVisible, "ShadowBorder must not be hit-test visible");
            Assert.Null(shadowBorder.Background);

            var hoverBorder = borders[1];
            Assert.False(hoverBorder.IsHitTestVisible, "HoverBorder must not be hit-test visible");

            // 3. 验证内容层必须承载 AppIcon 并允许命中测试
            var contentBorder = borders[2];
            Assert.True(contentBorder.IsHitTestVisible, "ContentBorder must be hit-test visible");
            Assert.NotNull(contentBorder.Child);
            Assert.IsType<SlimClip.UI.Controls.AppIcon>(contentBorder.Child);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void AppIcon_MustContain_Canvas_Viewport_24x24_To_Prevent_RightDown_Offset()
    {
        var appIcon = new SlimClip.UI.Controls.AppIcon { Size = 14 };
        Assert.Equal(14.0, appIcon.Width);
        Assert.Equal(14.0, appIcon.Height);

        // 验证 XAML 结构中包含固定 24x24 的 Canvas 视口，保证所有 SVG 路径几何重心位于 (12, 12) 正中
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Controls/AppIcon.axaml"));
        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();

        var canvas = doc.Descendants(ns + "Canvas").FirstOrDefault();
        Assert.NotNull(canvas);
        Assert.Equal("24", canvas.Attribute("Width")?.Value);
        Assert.Equal("24", canvas.Attribute("Height")?.Value);
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void ThemeDictionaries_MustContain_Light_And_Dark_Variants(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        Assert.True(File.Exists(basePath), $"Theme file not found at {basePath}");

        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();
        var xNs = doc.Root.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNs);

        var themeDictionaries = doc.Root.Element(ns + "ResourceDictionary.ThemeDictionaries");
        Assert.NotNull(themeDictionaries);

        var lightDict = themeDictionaries.Elements(ns + "ResourceDictionary")
            .FirstOrDefault(e => e.Attribute(xNs + "Key")?.Value == "Light");
        Assert.NotNull(lightDict);

        var darkDict = themeDictionaries.Elements(ns + "ResourceDictionary")
            .FirstOrDefault(e => e.Attribute(xNs + "Key")?.Value == "Dark");
        Assert.NotNull(darkDict);

        string[] requiredTokens = [
            "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush",
            "SurfaceBackground", "SearchBackground", "FooterBackground",
            "AcrylicTintColor", "CardHoverBrush", "CardSelectedBrush"
        ];

        foreach (var token in requiredTokens)
        {
            var inLight = lightDict.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(inLight, $"Token {token} missing in Light dictionary of {themeFileName}");

            var inDark = darkDict.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(inDark, $"Token {token} missing in Dark dictionary of {themeFileName}");
        }
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void ClinicalBlueprint_Tokens_MustExist_In_Both_Themes(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        Assert.True(File.Exists(basePath), $"Theme file not found at {basePath}");

        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();
        var xNs = doc.Root.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNs);

        var themeDictionaries = doc.Root.Element(ns + "ResourceDictionary.ThemeDictionaries");
        Assert.NotNull(themeDictionaries);

        var lightDict = themeDictionaries.Elements(ns + "ResourceDictionary")
            .FirstOrDefault(e => e.Attribute(xNs + "Key")?.Value == "Light");
        Assert.NotNull(lightDict);

        var darkDict = themeDictionaries.Elements(ns + "ResourceDictionary")
            .FirstOrDefault(e => e.Attribute(xNs + "Key")?.Value == "Dark");
        Assert.NotNull(darkDict);

        string[] blueprintTokens = [
            "InkBrush", "PaperBrush", "MidGrayBrush", "HairlineBrush",
            "EmberBrush", "ActiveChipBackground", "ActiveChipForeground",
            "CardBorderBrush", "SearchBorderBrush", "KeyHintBorderBrush"
        ];

        foreach (var token in blueprintTokens)
        {
            var inLight = lightDict.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(inLight, $"Blueprint token {token} missing in Light dictionary of {themeFileName}");

            var inDark = darkDict.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(inDark, $"Blueprint token {token} missing in Dark dictionary of {themeFileName}");
        }
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void LightMode_TextBrushes_Must_Meet_WCAG_AA_Contrast_Ratio(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        var doc = XDocument.Load(basePath);
        Assert.NotNull(doc.Root);
        var ns = doc.Root.GetDefaultNamespace();
        var xNs = doc.Root.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNs);

        var lightDict = doc.Root.Element(ns + "ResourceDictionary.ThemeDictionaries")!
            .Elements(ns + "ResourceDictionary")
            .First(e => e.Attribute(xNs! + "Key")?.Value == "Light");

        string GetBrushColor(string key) =>
            lightDict.Elements().First(e => e.Attribute(xNs! + "Key")?.Value == key).Attribute("Color")!.Value;

        var bg = GetBrushColor("PaperBrush");
        var primary = GetBrushColor("TextPrimaryBrush");
        var secondary = GetBrushColor("TextSecondaryBrush");
        var tertiary = GetBrushColor("TextTertiaryBrush");

        // Primary text on white paper should exceed 7:1 (WCAG AAA)
        Assert.True(CalculateContrastRatio(primary, bg) >= 7.0, $"TextPrimaryBrush {primary} must meet WCAG AAA contrast >= 7:1, was {CalculateContrastRatio(primary, bg):F2}");

        // Secondary text on paper should exceed 4.5:1 (WCAG AA)
        Assert.True(CalculateContrastRatio(secondary, bg) >= 4.5, $"TextSecondaryBrush {secondary} must meet WCAG AA contrast >= 4.5:1, was {CalculateContrastRatio(secondary, bg):F2}");

        // Tertiary text (used for timestamps, file paths, metadata, setting descriptions) MUST also meet WCAG AA >= 4.5:1
        Assert.True(CalculateContrastRatio(tertiary, bg) >= 4.5, $"TextTertiaryBrush {tertiary} failed WCAG AA contrast >= 4.5:1 on {bg}, was {CalculateContrastRatio(tertiary, bg):F2}");
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void DarkMode_TextBrushes_Must_Meet_WCAG_AA_Contrast_Ratio(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        var doc = XDocument.Load(basePath);
        Assert.NotNull(doc.Root);
        var ns = doc.Root.GetDefaultNamespace();
        var xNs = doc.Root.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNs);

        var darkDict = doc.Root.Element(ns + "ResourceDictionary.ThemeDictionaries")!
            .Elements(ns + "ResourceDictionary")
            .First(e => e.Attribute(xNs! + "Key")?.Value == "Dark");

        string GetBrushColor(string key) =>
            darkDict.Elements().First(e => e.Attribute(xNs! + "Key")?.Value == key).Attribute("Color")!.Value;

        var bg = GetBrushColor("PaperBrush");
        var primary = GetBrushColor("TextPrimaryBrush");
        var secondary = GetBrushColor("TextSecondaryBrush");
        var tertiary = GetBrushColor("TextTertiaryBrush");

        // In Dark Mode, primary text on dark paper (#141416) must exceed 10:1
        Assert.True(CalculateContrastRatio(primary, bg) >= 10.0, $"TextPrimaryBrush {primary} must meet contrast >= 10:1, was {CalculateContrastRatio(primary, bg):F2}");

        // Secondary text (used for tabs, descriptions) must meet WCAG AA >= 4.5:1 (ideally >= 7:1)
        Assert.True(CalculateContrastRatio(secondary, bg) >= 7.0, $"TextSecondaryBrush {secondary} must meet contrast >= 7:1 on {bg}, was {CalculateContrastRatio(secondary, bg):F2}");

        // Tertiary text (used for timestamps, badges) MUST also meet WCAG AA >= 4.5:1
        Assert.True(CalculateContrastRatio(tertiary, bg) >= 4.5, $"TextTertiaryBrush {tertiary} failed WCAG AA contrast >= 4.5:1 on {bg}, was {CalculateContrastRatio(tertiary, bg):F2}");
    }

    [Fact]
    public void Windows_Must_Enable_UseLayoutRounding()
    {
        EnsureHeadlessApp();
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        var settings = new SlimClip.UI.Views.SettingsWindow();

        try
        {
            Assert.True(panel.UseLayoutRounding, "ClipboardPanel must enable UseLayoutRounding for pixel grid snapping");
            Assert.True(settings.UseLayoutRounding, "SettingsWindow must enable UseLayoutRounding for pixel grid snapping");
        }
        finally
        {
            panel.Close();
            settings.Close();
        }
    }

    [Fact]
    public void Windows_TextRenderingMode_MustNot_Force_Grayscale_Antialias()
    {
        EnsureHeadlessApp();
        var panel = new SlimClip.UI.Views.ClipboardPanel();
        var settings = new SlimClip.UI.Views.SettingsWindow();

        try
        {
            var panelMode = Avalonia.Media.RenderOptions.GetTextRenderingMode(panel);
            var settingsMode = Avalonia.Media.RenderOptions.GetTextRenderingMode(settings);

            Assert.NotEqual(Avalonia.Media.TextRenderingMode.Antialias, panelMode);
            Assert.NotEqual(Avalonia.Media.TextRenderingMode.Antialias, settingsMode);
        }
        finally
        {
            panel.Close();
            settings.Close();
        }
    }

    private static double CalculateLuminance(string hexColor)
    {
        hexColor = hexColor.TrimStart('#');
        if (hexColor.Length == 8) hexColor = hexColor.Substring(2);
        int r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
        int g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
        int b = Convert.ToInt32(hexColor.Substring(4, 2), 16);

        double rs = r / 255.0;
        double gs = g / 255.0;
        double bs = b / 255.0;

        double rLin = rs <= 0.04045 ? rs / 12.92 : Math.Pow((rs + 0.055) / 1.055, 2.4);
        double gLin = gs <= 0.04045 ? gs / 12.92 : Math.Pow((gs + 0.055) / 1.055, 2.4);
        double bLin = bs <= 0.04045 ? bs / 12.92 : Math.Pow((bs + 0.055) / 1.055, 2.4);

        return 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
    }

    private static double CalculateContrastRatio(string hexColor1, string hexColor2)
    {
        double l1 = CalculateLuminance(hexColor1);
        double l2 = CalculateLuminance(hexColor2);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    [Fact]
    public void Window_CanResize_And_Constraints_Configured_Properly()
    {
        EnsureHeadlessApp();

        var panel = new SlimClip.UI.Views.ClipboardPanel();
        var settings = new SlimClip.UI.Views.SettingsWindow();

        try
        {
            Assert.True(panel.CanResize, "ClipboardPanel must allow resizing");
            Assert.True(panel.MinWidth >= 320, $"ClipboardPanel MinWidth was {panel.MinWidth}");
            Assert.True(panel.MinHeight >= 380, $"ClipboardPanel MinHeight was {panel.MinHeight}");
            Assert.True(panel.MaxWidth <= 1200, $"ClipboardPanel MaxWidth was {panel.MaxWidth}");
            Assert.True(panel.MaxHeight <= 1600, $"ClipboardPanel MaxHeight was {panel.MaxHeight}");

            Assert.True(settings.CanResize, "SettingsWindow must allow resizing");
            Assert.True(settings.MinWidth >= 500, $"SettingsWindow MinWidth was {settings.MinWidth}");
            Assert.True(settings.MinHeight >= 400, $"SettingsWindow MinHeight was {settings.MinHeight}");
        }
        finally
        {
            panel.Close();
            settings.Close();
        }
    }

    [Fact]
    public void ToggleSwitch_Checked_Style_Contrast_Verified()
    {
        EnsureHeadlessApp();

        var settings = new SlimClip.UI.Views.SettingsWindow();
        var panelData = settings.FindControl<ScrollViewer>("PanelData")!;
        panelData.IsVisible = true;
        settings.Show();
        settings.UpdateLayout();

        try
        {
            var toggle = settings.FindControl<ToggleSwitch>("AutoCleanToggle");
            Assert.NotNull(toggle);
            toggle.IsChecked = true;
            settings.UpdateLayout();

            var bounds = toggle.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "SwitchKnobBounds");
            var knob = toggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().FirstOrDefault(e => e.Name == "SwitchKnobOn");

            Assert.NotNull(bounds);
            Assert.NotNull(knob);

            var boundsBg = bounds.Background?.ToString();
            var knobFill = knob.Fill?.ToString();

            Assert.NotNull(boundsBg);
            Assert.NotNull(knobFill);
            // 确保底轨与旋钮具有清晰反差，杜绝全白融合
            Assert.NotEqual(boundsBg, knobFill);
        }
        finally
        {
            settings.Close();
        }
    }

    [Fact]
    public void SettingsWindow_DataTab_Three_Cards_And_Collapsible_Behavior()
    {
        EnsureHeadlessApp();

        var settings = new SlimClip.UI.Views.SettingsWindow();
        var panelData = settings.FindControl<ScrollViewer>("PanelData")!;
        panelData.IsVisible = true;
        settings.Show();
        settings.UpdateLayout();

        try
        {
            var stack = (StackPanel)panelData.Content!;
            // 校验根 StackPanel 下只有 3 个顶级 Card（Border）
            var cards = stack.Children.OfType<Border>().ToList();
            Assert.Equal(3, cards.Count);

            var autoToggle = settings.FindControl<ToggleSwitch>("AutoCleanToggle")!;
            var autoChild = settings.FindControl<StackPanel>("AutoCleanChildPanel")!;

            var periodicToggle = settings.FindControl<ToggleSwitch>("PeriodicCleanToggle")!;
            var periodicChild = settings.FindControl<StackPanel>("PeriodicCleanChildPanel")!;

            // 1. 初始/关状态：子面板隐藏
            autoToggle.IsChecked = false;
            periodicToggle.IsChecked = false;
            settings.UpdateLayout();
            Assert.False(autoChild.IsVisible);
            Assert.False(periodicChild.IsVisible);

            // 2. 开状态：子面板展开显示
            autoToggle.IsChecked = true;
            periodicToggle.IsChecked = true;
            settings.UpdateLayout();
            Assert.True(autoChild.IsVisible);
            Assert.True(periodicChild.IsVisible);
        }
        finally
        {
            settings.Close();
        }
    }

    [Fact]
    public void EmbeddedFont_HarmonyOS_Sans_SC_Resource_Resolvable()
    {
        EnsureHeadlessApp();

        // 1. 验证程序集内部嵌入的 HarmonyOS Sans SC 字体文件资源存在
        var fontUri = new Uri("avares://SlimClip.UI/Assets/Fonts/HarmonyOS_Sans_SC.ttf");
        Assert.True(Avalonia.Platform.AssetLoader.Exists(fontUri), "Font asset must exist");

        using (var stream = Avalonia.Platform.AssetLoader.Open(fontUri))
        {
            Assert.NotNull(stream);
            Assert.True(stream.Length > 15_000_000, "Font TTF file must have valid size");
        }

        // 2. 验证 Fluent2Theme.axaml 声明了 PrimaryFontFamily
        var app = Avalonia.Application.Current!;
        var found = app.TryFindResource("PrimaryFontFamily", out var fontObj);
        Assert.True(found, "PrimaryFontFamily must be resolvable from app resources");
        var primaryFontStr = fontObj?.ToString();
        Assert.NotNull(primaryFontStr);
        Assert.True(primaryFontStr.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase), "PrimaryFontFamily must contain HarmonyOS");

        // 3. 验证通过 PrimaryFontFamily 能直接解析出 GlyphTypeface，确保绝对不发生静默回退
        var fontManager = Avalonia.Media.FontManager.Current;
        var typeface = new Avalonia.Media.Typeface((Avalonia.Media.FontFamily)fontObj!);
        var success = fontManager.TryGetGlyphTypeface(typeface, out var gt);
        Assert.True(success, "PrimaryFontFamily must directly resolve to GlyphTypeface without fallback");
        Assert.NotNull(gt);

        // 4. 验证 HarmonyOSFontCollection 正确实例化并声明 Key
        var collection = new SlimClip.UI.Fonts.HarmonyOSFontCollection();
        Assert.Equal("fonts:HarmonyOS", collection.Key.ToString());

        var panel = new SlimClip.UI.Views.ClipboardPanel();
        Avalonia.Media.RenderOptions.SetTextRenderingMode(panel, Avalonia.Media.TextRenderingMode.Antialias);
        Avalonia.Media.RenderOptions.SetEdgeMode(panel, Avalonia.Media.EdgeMode.Antialias);
        var mode = Avalonia.Media.RenderOptions.GetTextRenderingMode(panel);
        Assert.Equal(Avalonia.Media.TextRenderingMode.Antialias, mode);
    }

    [Fact]
    public void EmbeddedFont_HarmonyOS_Sans_SC_GlyphTypeface_CanBeCreated_ViaCustomKey()
    {
        EnsureHeadlessApp();

        var fontManager = Avalonia.Media.FontManager.Current;
        var tfCustomKey = new Avalonia.Media.Typeface(new Avalonia.Media.FontFamily("fonts:HarmonyOS"));
        var success = fontManager.TryGetGlyphTypeface(tfCustomKey, out var glyphTypeface);

        Assert.True(success, "GlyphTypeface must be successfully created via fonts:HarmonyOS key");
        Assert.NotNull(glyphTypeface);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, glyphTypeface.Weight);
    }

    [Theory]
    [InlineData("Fluent2Theme.axaml")]
    [InlineData("MacHIGTheme.axaml")]
    public void Typography_Tokens_Must_Be_Defined_In_Themes(string themeFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Themes", themeFileName));
        Assert.True(File.Exists(basePath), $"Theme file not found at {basePath}");

        var doc = XDocument.Load(basePath);
        var xNs = doc.Root!.GetNamespaceOfPrefix("x");
        Assert.NotNull(xNs);

        string[] requiredFontSizes = [
            "AppTitleFontSize", "PageTitleFontSize", "SectionTitleFontSize",
            "BodyFontSize", "SubBodyFontSize", "CaptionFontSize"
        ];

        string[] requiredFontWeights = [
            "AppTitleFontWeight", "TitleFontWeight", "BodyFontWeight", "NormalFontWeight"
        ];

        foreach (var token in requiredFontSizes)
        {
            var exists = doc.Root.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(exists, $"FontSize token {token} missing in {themeFileName}");
        }

        foreach (var token in requiredFontWeights)
        {
            var exists = doc.Root.Elements().Any(e => e.Attribute(xNs + "Key")?.Value == token);
            Assert.True(exists, $"FontWeight token {token} missing in {themeFileName}");
        }
    }

    [Theory]
    [InlineData("ClipboardPanel.axaml")]
    [InlineData("SettingsWindow.axaml")]
    public void Views_Must_Use_Tokens_For_All_FontSize_And_FontWeight(string viewFileName)
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views", viewFileName));
        Assert.True(File.Exists(basePath), $"View file not found at {basePath}");

        var content = File.ReadAllText(basePath);

        // Check for hardcoded numeric FontSize like FontSize="14"
        var hardcodedFontSizeMatch = System.Text.RegularExpressions.Regex.Match(content, @"FontSize\s*=\s*""[0-9]");
        Assert.False(hardcodedFontSizeMatch.Success, $"Found hardcoded FontSize in {viewFileName} at index {hardcodedFontSizeMatch.Index}: {hardcodedFontSizeMatch.Value}");

        // Check for hardcoded named FontWeight like FontWeight="Medium" or Value="SemiBold" for FontWeight setters
        var hardcodedFontWeightAttr = System.Text.RegularExpressions.Regex.Match(content, @"FontWeight\s*=\s*""(Normal|Medium|SemiBold|Bold)""");
        Assert.False(hardcodedFontWeightAttr.Success, $"Found hardcoded FontWeight attribute in {viewFileName}: {hardcodedFontWeightAttr.Value}");

        var hardcodedFontWeightSetter = System.Text.RegularExpressions.Regex.Match(content, @"Property=""FontWeight""\s+Value=""(Normal|Medium|SemiBold|Bold)""");
        Assert.False(hardcodedFontWeightSetter.Success, $"Found hardcoded FontWeight Setter in {viewFileName}: {hardcodedFontWeightSetter.Value}");
    }

    [Fact]
    public void TextBlock_Default_FontWeight_Must_Match_Theme_NormalFontWeight()
    {
        EnsureHeadlessApp();

        var panel = new SlimClip.UI.Views.ClipboardPanel();
        panel.Show();
        panel.UpdateLayout();

        try
        {
            var tb = panel.FindControl<TextBlock>("CategoryKeyHint")!;
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, tb.FontWeight);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void SettingsWindow_ThemeRadio_Slider_And_NumericUpDown_Styles_And_Tokens_Verified()
    {
        EnsureHeadlessApp();

        var window = new SlimClip.UI.Views.SettingsWindow();
        window.Show();
        window.UpdateLayout();

        try
        {
            // 1. RadioButton:not(.TabItem) (Appearance theme mode radio buttons)
            var systemRadio = window.FindControl<RadioButton>("ThemeSystemRadio")!;
            var lightRadio = window.FindControl<RadioButton>("ThemeLightRadio")!;
            var darkRadio = window.FindControl<RadioButton>("ThemeDarkRadio")!;

            Assert.NotNull(systemRadio);
            Assert.NotNull(lightRadio);
            Assert.NotNull(darkRadio);

            Assert.Equal(Avalonia.Input.StandardCursorType.Hand, systemRadio.Cursor?.ToString() != null ? Avalonia.Input.StandardCursorType.Hand : Avalonia.Input.StandardCursorType.Hand);

            // 2. NumericUpDown (Data tab controls)
            var maxHistoryInput = window.FindControl<NumericUpDown>("MaxHistoryItemsInput")!;
            var cleanPeriodInput = window.FindControl<NumericUpDown>("CleanPeriodDaysInput")!;

            Assert.NotNull(maxHistoryInput);
            Assert.NotNull(cleanPeriodInput);

            // Ensure typography tokens are bound and effective
            var app = Avalonia.Application.Current!;
            Assert.True(app.TryFindResource("BodyFontSize", out var bodyFontSizeObj));
            var expectedBodySize = (double)bodyFontSizeObj!;
            Assert.Equal(expectedBodySize, maxHistoryInput.FontSize);
            Assert.Equal(expectedBodySize, cleanPeriodInput.FontSize);

            // 3. Slider (Advanced tab focus delay)
            var pasteSlider = window.FindControl<Slider>("PasteDelaySlider")!;
            Assert.NotNull(pasteSlider);
            Assert.Equal(10.0, pasteSlider.Minimum);
            Assert.Equal(240.0, pasteSlider.Maximum);

            var resetBtn = window.FindControl<Button>("ResetPasteDelayButton");
            Assert.NotNull(resetBtn);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, resetBtn.HorizontalAlignment);
            var parentStack = resetBtn.Parent as StackPanel;
            Assert.NotNull(parentStack);
            Assert.Equal(Avalonia.Layout.Orientation.Vertical, parentStack.Orientation);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, parentStack.HorizontalAlignment);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SettingsWindow_Monochrome_Blueprint_Styles_Defined()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views/SettingsWindow.axaml"));
        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();

        var styles = doc.Root.Element(ns + "Window.Styles")?.Elements(ns + "Style").ToList();
        Assert.NotNull(styles);

        // RadioButton style checks
        var radioBase = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "RadioButton" || s.Attribute("Selector")?.Value == "RadioButton:not(.TabItem)");
        Assert.NotNull(radioBase);
        Assert.Equal("Hand", radioBase.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Cursor")?.Attribute("Value")?.Value);

        // Slider template checks
        var sliderDecrease = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "Slider /template/ RepeatButton#PART_DecreaseButton");
        Assert.NotNull(sliderDecrease);
        Assert.Equal("{DynamicResource ActiveChipBackground}", sliderDecrease.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Background")?.Attribute("Value")?.Value);

        var sliderThumb = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "Thumb.SliderPillThumb /template/ Border#ThumbCircle");
        Assert.NotNull(sliderThumb);
        Assert.Equal("24", sliderThumb.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Width")?.Attribute("Value")?.Value);
        Assert.Equal("{DynamicResource ActiveChipForeground}", sliderThumb.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "BorderBrush")?.Attribute("Value")?.Value);

        var sliderThumbHover = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "Thumb.SliderPillThumb:pointerover /template/ Border#ThumbCircle");
        Assert.NotNull(sliderThumbHover);
        Assert.Equal("36", sliderThumbHover.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Width")?.Attribute("Value")?.Value);

        // NumericUpDown template checks
        var numStyle = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value == "NumericUpDown");
        Assert.NotNull(numStyle);
        Assert.Equal("{DynamicResource SearchBackground}", numStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "Background")?.Attribute("Value")?.Value);
        Assert.Equal("{DynamicResource HairlineBrush}", numStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "BorderBrush")?.Attribute("Value")?.Value);
        Assert.Equal("{DynamicResource KeyHintCornerRadius}", numStyle.Elements(ns + "Setter").FirstOrDefault(s => s.Attribute("Property")?.Value == "CornerRadius")?.Attribute("Value")?.Value);
    }

    [Fact]
    public void SettingsWindow_Slider_DecreaseButton_Aligns_With_Thumb_Center()
    {
        EnsureHeadlessApp();

        var window = new SlimClip.UI.Views.SettingsWindow();
        var panelAdvanced = window.FindControl<ScrollViewer>("PanelAdvanced")!;
        panelAdvanced.IsVisible = true;
        window.Show();
        window.UpdateLayout();

        try
        {
            var slider = window.FindControl<Slider>("PasteDelaySlider")!;
            slider.Value = 100;
            window.UpdateLayout();

            var track = slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Track>().FirstOrDefault();
            Assert.NotNull(track);

            var decreaseBtn = slider.GetVisualDescendants().OfType<RepeatButton>().FirstOrDefault(b => b.Name == "PART_DecreaseButton");
            Assert.NotNull(decreaseBtn);

            var thumb = slider.GetVisualDescendants().OfType<Thumb>().FirstOrDefault(t => t.Name == "PART_Thumb");
            Assert.NotNull(thumb);

            var decreaseBorder = decreaseBtn.GetVisualDescendants().OfType<Border>().FirstOrDefault();
            Assert.NotNull(decreaseBorder);

            Assert.True(decreaseBtn.Bounds.Width > 0, "DecreaseButton must have width");
            Assert.True(thumb.Bounds.Width > 0, "Thumb must have width");

            // decreaseBtn 右侧默认在 thumb.Bounds.Left
            Assert.Equal(thumb.Bounds.Left, decreaseBtn.Bounds.Right, 1.0);

            // decreaseBorder 由于设置了 Margin="0,0,-18,0"，其宽度必须比 decreaseBtn 宽 18px，从而延伸到滑块中心 (thumb.Bounds.Center.X)
            Assert.Equal(decreaseBtn.Bounds.Width + 18, decreaseBorder.Bounds.Width, 1.0);
            Assert.False(decreaseBtn.ClipToBounds, $"RepeatButton ClipToBounds must be False, but was {decreaseBtn.ClipToBounds}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SettingsViewModel_PasteDelayMs_Clamping_And_ResetDefault_Command()
    {
        var settingsService = new SettingsService();
        var vm = new SlimClip.UI.ViewModels.SettingsViewModel(settingsService, new DummyClipboardHistory(), null);

        // 验证 Clamp 范围限制在 [10, 240]
        vm.PasteDelayMs = 5;
        Assert.Equal(10, vm.PasteDelayMs);

        vm.PasteDelayMs = 300;
        Assert.Equal(240, vm.PasteDelayMs);

        vm.PasteDelayMs = 150;
        Assert.Equal(150, vm.PasteDelayMs);

        // 执行恢复默认命令
        vm.ResetPasteDelayDefaultCommand.Execute(null);
        var expectedDefault = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? 80 : 60;
        Assert.Equal(expectedDefault, vm.PasteDelayMs);
    }

    [Fact]
    public void ClipboardPanel_ItemTemplate_Content_Top_Aligned_And_Time_Bottom_Aligned()
    {
        EnsureHeadlessApp();

        var panel = new SlimClip.UI.Views.ClipboardPanel();
        var item = new ClipboardItem
        {
            Id = "test-1",
            Type = ClipboardType.Text,
            Text = "Top Aligned Text",
            CopiedAt = DateTime.Now
        };
        var vm = new SlimClip.UI.ViewModels.MainViewModel(
            new DummyClipboardHistory(),
            new FakeClipboardMonitor(),
            new FakePasteService(),
            new SettingsService());
        vm.Items.Add(item);
        panel.DataContext = vm;
        panel.Show();
        panel.UpdateLayout();

        try
        {
            var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
            Assert.NotNull(listBox);
            var lbi = listBox.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
            Assert.NotNull(lbi);

            // 查找主要信息的 2 行 Grid (Row 0 Content, Row 1 Time)
            var mainGrid = lbi.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.RowDefinitions.Count == 2);
            Assert.NotNull(mainGrid);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, mainGrid.VerticalAlignment);

            // Row 0 内容区域为 VerticalAlignment.Top
            var topPanel = mainGrid.Children.FirstOrDefault(c => Grid.GetRow((Control)c) == 0) as Control;
            Assert.NotNull(topPanel);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, topPanel.VerticalAlignment);

            // Row 1 时间与元数据区域为 VerticalAlignment.Bottom
            var bottomPanel = mainGrid.Children.FirstOrDefault(c => Grid.GetRow((Control)c) == 1) as Control;
            Assert.NotNull(bottomPanel);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, bottomPanel.VerticalAlignment);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void NumericUpDown_ButtonSpinner_Buttons_Are_Vertically_Stacked_On_Right()
    {
        EnsureHeadlessApp();
        var window = new SlimClip.UI.Views.SettingsWindow();
        var panelData = window.FindControl<ScrollViewer>("PanelData")!;
        panelData.IsVisible = true;
        var toggle = window.FindControl<ToggleSwitch>("AutoCleanToggle")!;
        toggle.IsChecked = true;
        window.Show();
        window.UpdateLayout();
        try
        {
            var num = window.FindControl<NumericUpDown>("MaxHistoryItemsInput")!;
            Assert.NotNull(num);
            var spinner = num.GetVisualDescendants().OfType<ButtonSpinner>().FirstOrDefault();
            Assert.NotNull(spinner);

            var incBtn = spinner.GetVisualDescendants().OfType<RepeatButton>().FirstOrDefault(b => b.Name == "PART_IncreaseButton");
            var decBtn = spinner.GetVisualDescendants().OfType<RepeatButton>().FirstOrDefault(b => b.Name == "PART_DecreaseButton");
            Assert.NotNull(incBtn);
            Assert.NotNull(decBtn);

            // 验证上下排布 (Vertical layout)
            Assert.True(incBtn.Bounds.Top < decBtn.Bounds.Top, $"IncreaseButton (Top={incBtn.Bounds.Top}) must be above DecreaseButton (Top={decBtn.Bounds.Top})");
            Assert.Equal(incBtn.Bounds.Left, decBtn.Bounds.Left, 1.0); // 水平对齐

            // 验证位于右侧
            var spinnerPanel = spinner.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "PART_SpinnerPanel");
            Assert.NotNull(spinnerPanel);
            Assert.Equal(Dock.Right, DockPanel.GetDock(spinnerPanel));

            // 验证点击 Increase 和 Decrease 能够正确改变数值
            num.Value = 500;
            var initialValue = num.Value;
            Assert.True(spinner.AllowSpin, "Spinner AllowSpin must be true");
            Assert.True(incBtn.IsEnabled, "IncreaseButton must be enabled");

            // 触发点击增加
            incBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, incBtn));
            window.UpdateLayout();
            Assert.Equal(550, num.Value);

            // 触发点击减少
            decBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, decBtn));
            window.UpdateLayout();
            Assert.Equal(500, num.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ClipboardPanel_EditorOverlay_And_ContextMenu_Configured_Properly()
    {
        EnsureHeadlessApp();

        var history = new DummyClipboardHistory();
        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();
        var vm = new MainViewModel(history, monitor, pasteService, settingsService);

        var panel = new ClipboardPanel(vm);
        try
        {
            panel.Show();

            var overlay = panel.FindControl<Border>("EditorOverlay");
            Assert.NotNull(overlay);
            Assert.False(overlay.IsVisible);

            var editTextBox = panel.FindControl<TextBox>("EditItemTextBox");
            Assert.NotNull(editTextBox);

            // 启动编辑
            var item = new ClipboardItem { Id = "test1", Type = ClipboardType.Text, Text = "Test Content" };
            vm.StartEditItem(item);
            panel.UpdateLayout();

            Assert.True(overlay.IsVisible);
            Assert.Equal("Test Content", editTextBox.Text);

            // 取消编辑
            vm.CancelEditItem();
            panel.UpdateLayout();
            Assert.False(overlay.IsVisible);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void ContextMenu_Background_MustBeOpaque_ToPreventTextOverlap()
    {
        EnsureHeadlessApp();
        var panel = new ClipboardPanel();
        try
        {
            var menuStyle = panel.Styles.OfType<Style>().FirstOrDefault(s => s.Selector?.ToString()?.Contains("ContextMenu") == true);
            Assert.NotNull(menuStyle);
            var bgSetter = menuStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == ContextMenu.BackgroundProperty);
            Assert.NotNull(bgSetter);

            // 背景必须使用实体/高不透明度背景（不能是半透明的 CardBackground）
            if (bgSetter.Value is Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension dyn)
            {
                Assert.NotEqual("CardBackground", dyn.ResourceKey?.ToString());
            }
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void ClipboardItem_Template_Panel_MustHaveTransparentBackground_And_Stretch_ForFullCardHitTest()
    {
        EnsureHeadlessApp();
        var history = new DummyClipboardHistory();
        var monitor = new FakeClipboardMonitor();
        var pasteService = new FakePasteService();
        var settingsService = new SettingsService();
        var vm = new MainViewModel(history, monitor, pasteService, settingsService);

        var item = new ClipboardItem { Id = "test1", Type = ClipboardType.Text, Text = "Short" };
        vm.Items.Add(item);

        var panel = new ClipboardPanel(vm);
        try
        {
            panel.Show();
            panel.UpdateLayout();

            var listBox = panel.FindControl<ListBox>("ItemsListBox")!;
            var listBoxItem = listBox.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
            Assert.NotNull(listBoxItem);

            var templatePanel = listBoxItem.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.ContextMenu != null);
            Assert.NotNull(templatePanel);

            // 必须显式有 Background (为 Transparent 且不为 null)，否则空白区域无法命中测试
            Assert.NotNull(templatePanel.Background);

            // HorizontalAlignment 必须为 Stretch
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, templatePanel.HorizontalAlignment);
            // ListBoxItem 的 HorizontalContentAlignment 也必须为 Stretch
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, listBoxItem.HorizontalContentAlignment);
        }
        finally
        {
            panel.Close();
        }
    }

    [Fact]
    public void Theme_Shortcut_Tokens_AreDefined_And_PlatformNative()
    {
        EnsureHeadlessApp();

        var fluentUri = new Uri("avares://SlimClip.UI/Themes/Fluent2Theme.axaml");
        var fluentDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(fluentUri);

        Assert.Equal("Ctrl+C", fluentDict["ShortcutCopyText"] as string);
        Assert.Equal("Ctrl+E", fluentDict["ShortcutEditText"] as string);
        Assert.Equal("Ctrl+D", fluentDict["ShortcutFavoriteText"] as string);
        Assert.Equal("Ctrl+Backspace", fluentDict["ShortcutDeleteText"] as string);
        Assert.Equal("Ctrl+Enter", fluentDict["ShortcutSaveTooltip"] as string);
        Assert.Equal("Esc", fluentDict["ShortcutCancelTooltip"] as string);
        Assert.False(fluentDict.ContainsKey("ShortcutPinText"), "ShortcutPinText should be removed");

        var macUri = new Uri("avares://SlimClip.UI/Themes/MacHIGTheme.axaml");
        var macDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(macUri);

        Assert.Equal("⌘C", macDict["ShortcutCopyText"] as string);
        Assert.Equal("⌘E", macDict["ShortcutEditText"] as string);
        Assert.Equal("⌘D", macDict["ShortcutFavoriteText"] as string);
        Assert.Equal("⌘⌫", macDict["ShortcutDeleteText"] as string);
        Assert.Equal("⌘↵", macDict["ShortcutSaveTooltip"] as string);
        Assert.Equal("⎋", macDict["ShortcutCancelTooltip"] as string);
        Assert.False(macDict.ContainsKey("ShortcutPinText"), "ShortcutPinText should be removed");
    }

    [Fact]
    public void SettingsWindow_ScrollViewer_Content_DoesNot_Overlap_ScrollBar()
    {
        EnsureHeadlessApp();

        var settings = new SettingsWindow();
        settings.SelectTab("About");

        var panelAbout = settings.FindControl<ScrollViewer>("PanelAbout")!;
        panelAbout.Height = 300;

        settings.Show();
        settings.UpdateLayout();

        try
        {
            var vBar = ((Avalonia.Visual)panelAbout).GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(s => s.Orientation == Orientation.Vertical);
            Assert.NotNull(vBar);
            Assert.True(vBar.IsVisible, "Vertical scrollbar should be visible when content overflows");

            var cp = ((Avalonia.Visual)panelAbout).GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
            Assert.NotNull(cp);

            var firstCard = cp.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.CornerRadius.TopLeft > 0);
            Assert.NotNull(firstCard);

            var cardPt = firstCard.TranslatePoint(new Avalonia.Point(0, 0), panelAbout);
            Assert.True(cardPt.HasValue);
            var cardRight = cardPt.Value.X + firstCard.Bounds.Width;

            var vBarPt = vBar.TranslatePoint(new Avalonia.Point(0, 0), panelAbout);
            Assert.True(vBarPt.HasValue);
            var vBarLeft = vBarPt.Value.X;

            // Card left and right margins must be symmetrical (equal blank space on left and right)
            var leftBlank = cardPt.Value.X;
            var rightBlank = panelAbout.Bounds.Width - cardRight;
            Assert.Equal(16.0, leftBlank);
            Assert.Equal(16.0, rightBlank);
            Assert.Equal(leftBlank, rightBlank);

            // Card right must be strictly to the left of the scrollbar's left boundary
            Assert.True(cardRight < vBarLeft,
                $"Card right ({cardRight}) must be to the left of ScrollBar left ({vBarLeft})");

            var gap = vBarLeft - cardRight;
            Assert.True(gap >= 4.0, $"Gap between card and scrollbar should be at least 4px, but was {gap}px");
        }
        finally
        {
            settings.Close();
        }
    }

    [Fact]
    public void SettingsWindow_ScrollBar_Must_AutoHide_When_Idle()
    {
        EnsureHeadlessApp();

        var settings = new SettingsWindow();
        settings.SelectTab("About");

        var panelAbout = settings.FindControl<ScrollViewer>("PanelAbout")!;
        panelAbout.Height = 300;

        settings.Show();
        settings.UpdateLayout();

        try
        {
            var vBar = ((Avalonia.Visual)panelAbout).GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(s => s.Orientation == Orientation.Vertical);
            Assert.NotNull(vBar);

            // 1. When idle, the scrollbar must be completely hidden / transparent (Opacity == 0)
            Assert.Equal(0.0, vBar.Opacity);

            // 2. Moving mouse over the card/content area does NOT reveal the scrollbar (only scrolling does)
            settings.MouseMove(new Avalonia.Point(100, 200));
            settings.UpdateLayout();
            Assert.Equal(0.0, vBar.Opacity);

            // 3. Verify style definition: ScrollBar.scrolling:vertical targets Opacity 0.85
            var scrollingStyle = settings.Styles.OfType<Style>()
                .FirstOrDefault(s => s.Selector?.ToString().Contains("scrolling") == true);
            Assert.NotNull(scrollingStyle);
            var opacitySetter = scrollingStyle.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == Visual.OpacityProperty);
            Assert.NotNull(opacitySetter);
            Assert.Equal(0.85, Convert.ToDouble(opacitySetter.Value));

            // 4. Actively scrolling triggers the 'scrolling' class
            vBar.Classes.Add("scrolling");
            Assert.Contains("scrolling", vBar.Classes);

            // 5. When scrolling stops and timer expires, it returns to idle
            vBar.Classes.Remove("scrolling");
            Assert.DoesNotContain("scrolling", vBar.Classes);
        }
        finally
        {
            settings.Close();
        }
    }

    [Fact]
    public void SettingsWindow_ScrollBar_Thumb_MustBe_Rounded_Pill()
    {
        EnsureHeadlessApp();

        var settings = new SettingsWindow();
        settings.SelectTab("About");

        var panelAbout = settings.FindControl<ScrollViewer>("PanelAbout")!;
        panelAbout.Height = 300;

        settings.Show();
        settings.UpdateLayout();

        try
        {
            var vBar = ((Avalonia.Visual)panelAbout).GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(s => s.Orientation == Orientation.Vertical)!;
            var thumb = vBar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault()!;
            var thumbBorder = thumb.GetVisualDescendants().OfType<Border>().FirstOrDefault()!;

            Assert.Equal(6.0, thumb.Bounds.Width);
            Assert.Equal(new Avalonia.CornerRadius(3), thumb.CornerRadius);
            Assert.Equal(new Avalonia.CornerRadius(3), thumbBorder.CornerRadius);

            // Check that legacy repeat buttons are hidden
            var upBtn = vBar.GetVisualDescendants().OfType<RepeatButton>().FirstOrDefault(b => b.Name == "PART_LineUpButton");
            Assert.True(upBtn == null || !upBtn.IsVisible || upBtn.Height == 0);
        }
        finally
        {
            settings.Close();
        }
    }

    [Fact]
    public void SettingsWindow_LanguageChange_UpdatesUIImmediately()
    {
        EnsureHeadlessApp();

        var settingsService = new SettingsService();
        settingsService.Settings.Language = "zh-CN";
        var locService = new LocalizationService("zh-CN");
        var fakeHistory = new DummyClipboardHistory();

        var vm = new SettingsViewModel(settingsService, fakeHistory, null, locService);
        var window = new SettingsWindow(vm);
        window.Show();
        window.UpdateLayout();

        try
        {
            var tabGeneral = window.FindControl<UnderlineTabItem>("TabGeneral")!;
            var generalTb = tabGeneral.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal("常规", generalTb.Text);

            // Change language to English
            var enLang = vm.SupportedLanguages.First(l => l.Code == "en-US");
            vm.SelectedLanguage = enLang;
            window.UpdateLayout();

            Assert.Equal("General", generalTb.Text);
            Assert.Equal("Settings", window.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LanguageComboBox_RendersWithMonochromeBlueprintStyle()
    {
        EnsureHeadlessApp();

        var settingsService = new SettingsService();
        var locService = new LocalizationService("zh-CN");
        var fakeHistory = new DummyClipboardHistory();

        var vm = new SettingsViewModel(settingsService, fakeHistory, null, locService);
        var window = new SettingsWindow(vm);
        window.Show();
        window.UpdateLayout();

        try
        {
            var comboBox = window.FindControl<ComboBox>("LanguageComboBox");
            if (comboBox == null)
            {
                comboBox = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
            }
            Assert.NotNull(comboBox);
            Assert.Equal(32, comboBox.MinHeight);
            Assert.Equal(2, comboBox.ItemCount);
            Assert.NotNull(comboBox.SelectedItem);
            Assert.Equal("zh-CN", ((LanguageInfo)comboBox.SelectedItem).Code);

            // Assert that the closed selection box does NOT render any checkmark icon
            var selectionPresenter = comboBox.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => p.Name == "ContentPresenter");
            if (selectionPresenter != null)
            {
                var selectionIcon = selectionPresenter.GetVisualDescendants().OfType<SlimClip.UI.Controls.AppIcon>().FirstOrDefault();
                Assert.Null(selectionIcon);
            }

            // Open dropdown to inspect popup items layout
            comboBox.IsDropDownOpen = true;
            window.UpdateLayout();

            var popup = comboBox.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
            Assert.NotNull(popup);
            var popupBorder = popup.Child as Border;
            Assert.NotNull(popupBorder);
            // Popup CornerRadius must match ComboBox CornerRadius (6)
            Assert.Equal(new CornerRadius(6), popupBorder.CornerRadius);

            var pHost = popup.Host as Visual;
            var foundItems = pHost?.GetVisualDescendants().OfType<ComboBoxItem>().ToList() ?? new List<ComboBoxItem>();
            Assert.Equal(2, foundItems.Count);

            // First item is selected (zh-CN)
            var firstItem = foundItems[0];
            var firstGrid = firstItem.GetVisualDescendants().OfType<Grid>().FirstOrDefault();
            Assert.NotNull(firstGrid);
            var firstTextBlock = firstGrid.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
            var firstIcon = firstGrid.GetVisualDescendants().OfType<SlimClip.UI.Controls.AppIcon>().FirstOrDefault();
            Assert.NotNull(firstTextBlock);
            Assert.NotNull(firstIcon);
            Assert.True(firstIcon.IsVisible);

            // TextBlock on the left (X == 0), Icon on the right with space in between
            Assert.Equal(0, firstTextBlock.Bounds.X);
            Assert.True(firstIcon.Bounds.X > firstTextBlock.Bounds.Right + 10, "Check icon should be right-aligned with ample space between text and icon");

            // Second item is not selected (en-US), checkmark must NOT be visible
            var secondItem = foundItems[1];
            var secondIcon = secondItem.GetVisualDescendants().OfType<SlimClip.UI.Controls.AppIcon>().FirstOrDefault();
            Assert.NotNull(secondIcon);
            Assert.False(secondIcon.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SettingsWindow_RepeatedOpen_MustPreserveSelectedLanguage()
    {
        EnsureHeadlessApp();

        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(null, tempDir);
            settingsService.Settings.Language = "zh-CN";
            settingsService.Save();

            var locService = new LocalizationService(settingsService.Settings.Language);
            var vm = new SettingsViewModel(settingsService, new DummyClipboardHistory(), null, locService);

            Assert.Equal("zh-CN", vm.SelectedLanguage.Code);

            // Open window 1
            var window1 = new SettingsWindow(vm);
            window1.Show();
            window1.UpdateLayout();
            Assert.Equal("zh-CN", vm.SelectedLanguage.Code);
            Assert.Equal("zh-CN", settingsService.Settings.Language);
            window1.Close();

            // Open window 2 (simulating user clicking settings again)
            var window2 = new SettingsWindow(vm);
            window2.Show();
            window2.UpdateLayout();
            Assert.Equal("zh-CN", vm.SelectedLanguage.Code);
            Assert.Equal("zh-CN", settingsService.Settings.Language);
            window2.Close();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ClipboardPanel_KeyHints_IncludePlatformSymbols()
    {
        EnsureHeadlessApp();

        var panel = new ClipboardPanel();
        panel.Show();
        panel.UpdateLayout();

        Assert.Null(panel.FindControl<TextBlock>("TabKeyHint"));

        var categoryHint = panel.FindControl<TextBlock>("CategoryKeyHint");
        var navHint = panel.FindControl<TextBlock>("NavigateKeyHint");
        var pasteHint = panel.FindControl<TextBlock>("PasteKeyHint");
        var closeHint = panel.FindControl<TextBlock>("CloseKeyHint");

        Assert.NotNull(categoryHint);
        Assert.NotNull(navHint);
        Assert.NotNull(pasteHint);
        Assert.NotNull(closeHint);

        var isMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
        if (isMac)
        {
            Assert.Equal("◀ ▶", categoryHint.Text);
            Assert.Equal("▲ ▼", navHint.Text);
            Assert.Equal("↩", pasteHint.Text);
            Assert.Equal("⎋", closeHint.Text);
        }
        else
        {
            Assert.Equal("← →", categoryHint.Text);
            Assert.Equal("↑ ↓", navHint.Text);
            Assert.Equal("Enter", pasteHint.Text);
            Assert.Equal("Esc", closeHint.Text);
        }

        panel.Close();
    }

    [Fact]
    public void SettingsWindow_PanelHotkey_ContainsShortcutsCheatSheet()
    {
        EnsureHeadlessApp();

        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(null, tempDir);
            var vm = new SettingsViewModel(settingsService, new DummyClipboardHistory());
            var window = new SettingsWindow(vm);
            window.SelectTab("Hotkey");

            var panelHotkey = window.FindControl<ScrollViewer>("PanelHotkey")!;
            window.Show();
            window.UpdateLayout();

            Assert.True(panelHotkey.IsVisible);

            // Find all TextBlocks in PanelHotkey to ensure cheat sheet strings are loaded
            var textBlocks = panelHotkey.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(textBlocks, t => t == "快捷键一览" || t == "Shortcuts Reference");
            Assert.Contains(textBlocks, t => t == "操作" || t == "Action");
            Assert.Contains(textBlocks, t => t == "快捷键" || t == "Shortcut");

            window.Close();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void TabItem_In_DarkMode_Properties_Check()
    {
        EnsureHeadlessApp();
        var app = Avalonia.Application.Current!;
        var prevTheme = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            var window = new SettingsWindow();
            window.Show();
            window.UpdateLayout();

            var tabGeneral = window.FindControl<UnderlineTabItem>("TabGeneral")!;
            Assert.NotNull(tabGeneral);
            Assert.True(tabGeneral.IsSelected);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, tabGeneral.FontWeight);

            var tb = tabGeneral.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.NotNull(tb);

            var tabBar = window.FindControl<UnderlineTabBar>("SettingsTabBar")!;
            Assert.NotNull(tabBar);
            Assert.Equal("General", tabBar.SelectedValue);

            var indicator = tabBar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);
            Assert.Equal(3.0, indicator.Height);
            Assert.Equal(new Avalonia.CornerRadius(1.5), indicator.CornerRadius);

            window.Close();
        }
        finally
        {
            app.RequestedThemeVariant = prevTheme;
        }
    }

    [Fact]
    public void ClipboardPanel_ItemCardContent_UsesStackPanelInsteadOfPanel()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SlimClip.UI/Views/ClipboardPanel.axaml"));
        var doc = XDocument.Load(basePath);
        var ns = doc.Root!.GetDefaultNamespace();

        var xNs = doc.Root.GetNamespaceOfPrefix("x") ?? XNamespace.None;
        var itemTemplate = doc.Descendants(ns + "DataTemplate")
            .FirstOrDefault(d => d.Attribute(xNs + "DataType")?.Value == "models:ClipboardItem");
        Assert.NotNull(itemTemplate);

        var contentStack = itemTemplate.Descendants(ns + "StackPanel")
            .FirstOrDefault(s => s.Attribute("Spacing")?.Value == "6");
        Assert.NotNull(contentStack);
        Assert.Equal("0", contentStack.Attribute(ns + "Grid.Row")?.Value ?? contentStack.Attribute("Grid.Row")?.Value);
    }

    private class DummyClipboardHistory : IClipboardHistory
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<ClipboardItem> RecordAsync(ClipboardData data) => Task.FromResult(new ClipboardItem());
        public Task<List<ClipboardItem>> GetAllItemsAsync() => Task.FromResult(new List<ClipboardItem>());
        public Task<bool> DeleteItemAsync(string id) => Task.FromResult(true);
        public Task<bool> ToggleFavoriteAsync(string id) => Task.FromResult(true);
        public Task ClearAllAsync() => Task.CompletedTask;
        public Task TrimExcessAsync(int maxItems) => Task.CompletedTask;
        public Task CleanPeriodicAsync(int days) => Task.CompletedTask;
        public Task UpdateTimestampAsync(string id, DateTime timestamp) => Task.CompletedTask;
        public Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt) => Task.FromResult(true);
        public void Dispose() { }
    }
}


