using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SlimClip.UI.Controls;

/// <summary>
/// Segmented tab item for <see cref="UnderlineTabBar"/>.
/// Features:
/// 1. Focus-immune (Focusable=false) to avoid stealing keyboard focus from SearchBox.
/// 2. Hover feedback with 6px rounded background and soft ambient shadow (0 2 6 #20000000).
/// 3. Constant SemiBold font weight across all states (unselected, hover, selected).
/// 4. Dynamic foreground brush switching (TextSecondaryBrush -> TextPrimaryBrush).
/// 5. Precise text content bounds calculation for the bottom underline indicator.
/// </summary>
[PseudoClasses(":selected")]
public class UnderlineTabItem : ContentControl
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<UnderlineTabItem, string?>(nameof(Value));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<UnderlineTabItem, bool>(nameof(IsSelected));

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private Border? _hoverBorder;
    private ContentPresenter? _contentPresenter;

    static UnderlineTabItem()
    {
        FocusableProperty.OverrideDefaultValue<UnderlineTabItem>(false);
        CursorProperty.OverrideDefaultValue<UnderlineTabItem>(new Cursor(StandardCursorType.Hand));
        PaddingProperty.OverrideDefaultValue<UnderlineTabItem>(new Thickness(14, 6));
        BackgroundProperty.OverrideDefaultValue<UnderlineTabItem>(Brushes.Transparent);
        FontSizeProperty.OverrideDefaultValue<UnderlineTabItem>(13.0);
        FontWeightProperty.OverrideDefaultValue<UnderlineTabItem>(FontWeight.SemiBold);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<UnderlineTabItem>(HorizontalAlignment.Center);
        VerticalContentAlignmentProperty.OverrideDefaultValue<UnderlineTabItem>(VerticalAlignment.Center);

        IsSelectedProperty.Changed.AddClassHandler<UnderlineTabItem>((x, e) => x.OnIsSelectedChanged(e));
        IsPointerOverProperty.Changed.AddClassHandler<UnderlineTabItem>((x, _) => x.UpdateVisualState());
    }

    public UnderlineTabItem()
    {
        Template = new Avalonia.Controls.Templates.FuncControlTemplate<UnderlineTabItem>((parent, _) =>
        {
            // 1. Hover backdrop & ambient soft shadow layer
            var hoverBorder = new Border
            {
                Name = "PART_HoverBorder",
                CornerRadius = new CornerRadius(6),
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = 2,
                    Blur = 6,
                    Spread = 0,
                    Color = Color.FromArgb(0x20, 0, 0, 0)
                }),
                Opacity = 0.0,
                IsHitTestVisible = false,
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }
                }
            };

            // 2. Content presentation layer
            var contentPresenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = parent[!ContentProperty],
                [!ContentPresenter.ContentTemplateProperty] = parent[!ContentTemplateProperty],
                [!ContentPresenter.PaddingProperty] = parent[!PaddingProperty],
                [!ContentPresenter.HorizontalAlignmentProperty] = parent[!HorizontalContentAlignmentProperty],
                [!ContentPresenter.VerticalAlignmentProperty] = parent[!VerticalContentAlignmentProperty],
                Background = Brushes.Transparent,
                RecognizesAccessKey = true
            };

            var rootPanel = new Panel
            {
                Background = Brushes.Transparent,
                Children = { hoverBorder, contentPresenter }
            };

            parent._hoverBorder = hoverBorder;
            parent._contentPresenter = contentPresenter;
            parent.UpdateVisualState();

            return rootPanel;
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var parentBar = this.FindAncestorOfType<UnderlineTabBar>();
        if (parentBar != null && !string.IsNullOrEmpty(parentBar.SelectedValue))
        {
            if (string.Equals(Value, parentBar.SelectedValue, StringComparison.OrdinalIgnoreCase))
            {
                IsSelected = true;
            }
        }
        UpdateVisualState();
    }

    private void OnIsSelectedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        PseudoClasses.Set(":selected", e.GetNewValue<bool>());
        UpdateVisualState();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var parentBar = this.FindAncestorOfType<UnderlineTabBar>()
                ?? this.FindLogicalAncestorOfType<UnderlineTabBar>() as UnderlineTabBar
                ?? Parent as UnderlineTabBar;

            if (parentBar != null)
            {
                parentBar.SelectTabItem(this, animated: true);
                e.Handled = true;
            }
        }
    }

    public void UpdateVisualState()
    {
        if (_hoverBorder == null) return;

        // Resolve hover background brush from theme resources
        if (this.TryFindResource("CardHoverBrush", out var hoverBrush) && hoverBrush is IBrush hb)
        {
            _hoverBorder.Background = hb;
        }
        else
        {
            _hoverBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
        }

        // Hover opacity transition
        _hoverBorder.Opacity = IsPointerOver ? 1.0 : 0.0;

        // Font weight is locked to SemiBold across all states
        FontWeight = FontWeight.SemiBold;

        // Dynamic text foreground
        if (IsSelected || IsPointerOver)
        {
            if (this.TryFindResource("TextPrimaryBrush", out var primaryBrush) && primaryBrush is IBrush pb)
            {
                Foreground = pb;
            }
            else
            {
                Foreground = Brushes.Black;
            }
        }
        else
        {
            if (this.TryFindResource("TextSecondaryBrush", out var secondaryBrush) && secondaryBrush is IBrush sb)
            {
                Foreground = sb;
            }
            else
            {
                Foreground = Brushes.Gray;
            }
        }
    }

    /// <summary>
    /// Computes the horizontal target bounds (X and Width) of the text content inside this tab item,
    /// relative to the specified coordinate space (typically the parent <see cref="UnderlineTabBar"/>).
    /// The returned indicator width hugs the text content with 4px margin on both sides (total +8px).
    /// </summary>
    public bool TryGetIndicatorBounds(Visual relativeTo, out double x, out double width)
    {
        x = 0;
        width = 0;

        var textVisual = this.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
        if (textVisual != null && textVisual.Bounds.Width > 0)
        {
            var pt = textVisual.TranslatePoint(new Point(0, 0), relativeTo);
            if (pt.HasValue)
            {
                double textWidth = textVisual.Bounds.Width;
                x = pt.Value.X - 4.0;
                width = textWidth + 8.0;
                return true;
            }
        }

        // Fallback: if item itself has laid out bounds
        if (Bounds.Width > 0)
        {
            var pt = this.TranslatePoint(new Point(0, 0), relativeTo);
            if (pt.HasValue)
            {
                double estimatedTextWidth = Math.Max(24.0, Bounds.Width - (Padding.Left + Padding.Right));
                double indicatorWidth = estimatedTextWidth + 8.0;
                x = pt.Value.X + (Bounds.Width - indicatorWidth) / 2.0;
                width = indicatorWidth;
                return true;
            }
        }

        return false;
    }
}
