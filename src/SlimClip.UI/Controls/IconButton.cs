using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlimClip.UI.Controls;

/// <summary>
/// 统一抽象的现代化小图标按钮控件：
/// 1. 严格居中呈现矢量语义图标（基于 AppIcon 24x24 视口）；
/// 2. 悬停（Hover）时呈现平滑过渡的圆角背景与发光微阴影；
/// 3. 悬浮阴影绝对中心对齐（OffsetX=0, OffsetY=0），与图标重心完美吻合；
/// 4. 采用独立阴影图层 + Opacity 双通道平滑淡入淡出，彻底杜绝首帧与退出时的浅色跳闪；
/// 5. 激活态（IsActive）呈现高对比立体胶囊徽章样式（如置顶白底黑字/黑底白字），清晰醒目；
/// 6. 允许自由设置常规、悬浮与激活态的颜色画刷。
/// </summary>
[PseudoClasses(":active")]
public class IconButton : Button
{
    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<IconButton, string>(nameof(IconKind), "Search");

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconButton, double>(nameof(IconSize), 14.0);

    public static readonly StyledProperty<string?> ToolTipTextProperty =
        AvaloniaProperty.Register<IconButton, string?>(nameof(ToolTipText));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<IconButton, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> NormalForegroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(NormalForeground));

    public static readonly StyledProperty<IBrush?> HoverForegroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(HoverForeground));

    public static readonly StyledProperty<IBrush?> HoverBackgroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(HoverBackground));

    public static readonly StyledProperty<IBrush?> ActiveForegroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(ActiveForeground));

    public static readonly StyledProperty<IBrush?> ActiveBackgroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(ActiveBackground));

    public static readonly StyledProperty<Color?> ShadowColorProperty =
        AvaloniaProperty.Register<IconButton, Color?>(nameof(ShadowColor));

    public string IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string? ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? NormalForeground
    {
        get => GetValue(NormalForegroundProperty);
        set => SetValue(NormalForegroundProperty, value);
    }

    public IBrush? HoverForeground
    {
        get => GetValue(HoverForegroundProperty);
        set => SetValue(HoverForegroundProperty, value);
    }

    public IBrush? HoverBackground
    {
        get => GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    public IBrush? ActiveForeground
    {
        get => GetValue(ActiveForegroundProperty);
        set => SetValue(ActiveForegroundProperty, value);
    }

    public IBrush? ActiveBackground
    {
        get => GetValue(ActiveBackgroundProperty);
        set => SetValue(ActiveBackgroundProperty, value);
    }

    public Color? ShadowColor
    {
        get => GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    private Border? _shadowBorder;
    private Border? _hoverBorder;
    private Border? _contentBorder;
    private AppIcon? _appIcon;

    static IconButton()
    {
        WidthProperty.OverrideDefaultValue<IconButton>(26.0);
        HeightProperty.OverrideDefaultValue<IconButton>(26.0);
        MinWidthProperty.OverrideDefaultValue<IconButton>(0.0);
        MinHeightProperty.OverrideDefaultValue<IconButton>(0.0);
        PaddingProperty.OverrideDefaultValue<IconButton>(new Thickness(0));
        CornerRadiusProperty.OverrideDefaultValue<IconButton>(new CornerRadius(6.0));
        BackgroundProperty.OverrideDefaultValue<IconButton>(Brushes.Transparent);
        BorderThicknessProperty.OverrideDefaultValue<IconButton>(new Thickness(0));
        CursorProperty.OverrideDefaultValue<IconButton>(new Cursor(StandardCursorType.Hand));
        HorizontalAlignmentProperty.OverrideDefaultValue<IconButton>(HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<IconButton>(VerticalAlignment.Center);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<IconButton>(HorizontalAlignment.Center);
        VerticalContentAlignmentProperty.OverrideDefaultValue<IconButton>(VerticalAlignment.Center);
        ClipToBoundsProperty.OverrideDefaultValue<IconButton>(false);

        // 统一监听指针悬浮状态变更，彻底避免进入子元素时的事件重复抖动与闪烁
        IsPointerOverProperty.Changed.AddClassHandler<IconButton>((btn, _) => btn.UpdateVisualState());
    }

    public IconButton()
    {
        FocusAdorner = null;
        Template = new Avalonia.Controls.Templates.FuncControlTemplate<IconButton>((parent, _) =>
        {
            var icon = new AppIcon
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                [!AppIcon.KindProperty] = parent[!IconKindProperty],
                [!AppIcon.SizeProperty] = parent[!IconSizeProperty]
            };

            // 1. 独立阴影层：使用 Opacity 进行 120ms 硬件平滑插值，彻底消除 BoxShadow 属性插值产生的浅色/白色跳闪
            var shadowBorder = new Border
            {
                ClipToBounds = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                [!Border.WidthProperty] = parent[!WidthProperty],
                [!Border.HeightProperty] = parent[!HeightProperty],
                [!Border.CornerRadiusProperty] = parent[!CornerRadiusProperty],
                IsHitTestVisible = false,
                Opacity = 0.0,
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }
                }
            };

            // 2. 独立悬浮背景层：承载悬浮圆角底色，纯通过 Opacity 进行 120ms 硬件平滑插值，
            // 彻底废除 Background 的 BrushTransition（避免 Transparent #00FFFFFF 线性插值产生的纯白杂质与浅色发光环闪烁）
            var hoverBorder = new Border
            {
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                [!Border.WidthProperty] = parent[!WidthProperty],
                [!Border.HeightProperty] = parent[!HeightProperty],
                [!Border.CornerRadiusProperty] = parent[!CornerRadiusProperty],
                IsHitTestVisible = false,
                Opacity = 0.0,
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }
                }
            };

            // 3. 内容与交互层：承载图标与常态/激活态底色，同时负责接收命中测试
            var contentBorder = new Border
            {
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                [!Border.WidthProperty] = parent[!WidthProperty],
                [!Border.HeightProperty] = parent[!HeightProperty],
                [!Border.CornerRadiusProperty] = parent[!CornerRadiusProperty],
                Background = Brushes.Transparent,
                Child = icon,
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(100) }
                }
            };

            var rootPanel = new Panel
            {
                ClipToBounds = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { shadowBorder, hoverBorder, contentBorder }
            };

            parent._shadowBorder = shadowBorder;
            parent._hoverBorder = hoverBorder;
            parent._contentBorder = contentBorder;
            parent._appIcon = icon;
            parent.UpdateVisualState();

            return rootPanel;
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ToolTipTextProperty)
        {
            var tip = change.GetNewValue<string?>();
            if (!string.IsNullOrEmpty(tip))
            {
                ToolTip.SetTip(this, tip);
            }
            else
            {
                ClearValue(ToolTip.TipProperty);
            }
        }
        else if (change.Property == IsActiveProperty)
        {
            PseudoClasses.Set(":active", change.GetNewValue<bool>());
            UpdateVisualState();
        }
        else if (change.Property == NormalForegroundProperty ||
                 change.Property == HoverForegroundProperty ||
                 change.Property == HoverBackgroundProperty ||
                 change.Property == ActiveForegroundProperty ||
                 change.Property == ActiveBackgroundProperty ||
                 change.Property == ShadowColorProperty ||
                 change.Property == ForegroundProperty ||
                 change.Property == BackgroundProperty)
        {
            UpdateVisualState();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_contentBorder != null)
        {
            _contentBorder.Opacity = 1.0;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_contentBorder != null)
        {
            _contentBorder.Opacity = 0.75;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_contentBorder != null)
        {
            _contentBorder.Opacity = 1.0;
        }
    }

    public BoxShadow GetEffectiveShadow(bool isHovered)
    {
        var iconBrush = GetEffectiveIconBrush(true, IsActive);
        var effectiveColor = ComputeEffectiveShadowColor(iconBrush);

        // 绝对中心对齐：OffsetX = 0, OffsetY = 0，与居中图标几何重心完全吻合
        return new BoxShadow
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = 4,
            Spread = 0,
            Color = effectiveColor
        };
    }

    public IBrush GetEffectiveIconBrush(bool isHovered, bool isActive)
    {
        if (isHovered && HoverForeground != null)
        {
            return HoverForeground;
        }

        if (isActive)
        {
            if (ActiveForeground != null)
            {
                return ActiveForeground;
            }

            // 激活态默认反色高亮（如白字/黑字），醒目而高级
            if (this.TryFindResource("ActiveChipForeground", out var activeFgRes) && activeFgRes is IBrush afb)
            {
                return afb;
            }
            return Brushes.White;
        }

        if (NormalForeground != null)
        {
            return NormalForeground;
        }

        if (Foreground != null)
        {
            return Foreground;
        }

        this.TryFindResource("TextSecondaryBrush", out var res);
        return res as IBrush ?? Brushes.Gray;
    }

    public IBrush GetEffectiveBackground(bool isHovered, bool isActive)
    {
        if (isHovered)
        {
            if (HoverBackground != null)
            {
                return HoverBackground;
            }

            if (isActive)
            {
                // 激活状态下悬停：保持胶囊底色
                if (ActiveBackground != null)
                {
                    return ActiveBackground;
                }
                if (this.TryFindResource("ActiveChipBackground", out var actHoverRes) && actHoverRes is IBrush ahb)
                {
                    return ahb;
                }
            }

            this.TryFindResource("CardHoverBrush", out var hoverRes);
            return hoverRes as IBrush ?? new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
        }

        if (isActive)
        {
            if (ActiveBackground != null)
            {
                return ActiveBackground;
            }

            // 激活态默认鲜明徽章胶囊背景（黑白为锚，显著可见）
            if (this.TryFindResource("ActiveChipBackground", out var activeBgRes) && activeBgRes is IBrush abb)
            {
                return abb;
            }
            return new SolidColorBrush(Color.FromArgb(230, 10, 10, 10));
        }

        return Background ?? Brushes.Transparent;
    }

    public void UpdateVisualState()
    {
        if (_shadowBorder == null || _hoverBorder == null || _contentBorder == null || _appIcon == null)
            return;

        var isHovered = IsPointerOver;
        var isActive = IsActive;

        // 1. 图标前景色
        var iconBrush = GetEffectiveIconBrush(isHovered, isActive);
        _appIcon.Foreground = iconBrush;

        // 2. 常态/激活态底色（在 contentBorder 上呈现）
        var baseBg = GetEffectiveBackground(false, isActive);
        _contentBorder.Background = baseBg;

        // 3. 悬浮底色（在 hoverBorder 上呈现，纯硬件 Opacity 渐变）
        var hoverBg = GetEffectiveBackground(true, isActive);
        _hoverBorder.Background = hoverBg;
        _hoverBorder.Opacity = isHovered ? 1.0 : 0.0;

        // 4. 悬浮微光晕阴影：通过独立层 Opacity 硬件加速平滑淡入淡出，绝对中心对称，绝无白闪/跳阶
        var shadow = GetEffectiveShadow(isHovered);
        _shadowBorder.BoxShadow = new BoxShadows(shadow);
        _shadowBorder.Opacity = isHovered ? 1.0 : 0.0;
    }

    public Color ComputeEffectiveShadowColor(IBrush? currentIconBrush)
    {
        if (ShadowColor.HasValue)
        {
            return ShadowColor.Value;
        }

        // 提取图标画刷的基础色彩
        Color baseColor = Colors.Black;
        if (currentIconBrush is ISolidColorBrush scb)
        {
            baseColor = scb.Color;
        }
        else
        {
            return Color.FromArgb(28, 0, 0, 0);
        }

        // 判断是否为黑白灰等中性单色
        if (baseColor.R == baseColor.G && baseColor.G == baseColor.B)
        {
            // 常规中性微柔阴影（纯净透明度，避免视觉脏乱）
            return Color.FromArgb(28, 0, 0, 0);
        }

        // 彩色图标（如删除红色）：同色相平滑光晕
        return Color.FromArgb(46, baseColor.R, baseColor.G, baseColor.B);
    }
}
