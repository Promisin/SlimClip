using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SlimClip.UI.Animation;

namespace SlimClip.UI.Controls;

/// <summary>
/// Segmented tab bar control with an animated bottom underline indicator.
/// Completely decoupled from specific views, encapsulates ElasticPillAnimator,
/// and is focus-immune (Focusable=false).
/// </summary>
[TemplatePart("PART_Indicator", typeof(Border))]
[TemplatePart("PART_ItemsPresenter", typeof(ItemsPresenter))]
public class UnderlineTabBar : ItemsControl
{
    public static readonly StyledProperty<string?> SelectedValueProperty =
        AvaloniaProperty.Register<UnderlineTabBar, string?>(nameof(SelectedValue), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<UnderlineTabBar, int>(nameof(SelectedIndex), defaultValue: -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<UnderlineTabBar, double>(nameof(ItemSpacing), defaultValue: 4.0);

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<UnderlineTabBar, IBrush?>(nameof(IndicatorBrush));

    public string? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    private Border? _indicator;
    private ElasticPillAnimator? _animator;
    private UnderlineTabItem? _lastSelectedTabItem;
    private bool _isUpdatingInternally;

    static UnderlineTabBar()
    {
        FocusableProperty.OverrideDefaultValue<UnderlineTabBar>(false);
        BackgroundProperty.OverrideDefaultValue<UnderlineTabBar>(Brushes.Transparent);
        SelectedValueProperty.Changed.AddClassHandler<UnderlineTabBar>((x, e) => x.OnSelectedValueChanged(e));
        SelectedIndexProperty.Changed.AddClassHandler<UnderlineTabBar>((x, e) => x.OnSelectedIndexChanged(e));
        IndicatorBrushProperty.Changed.AddClassHandler<UnderlineTabBar>((x, _) => x.UpdateIndicatorBrush());
    }

    public UnderlineTabBar()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ItemSpacing
        });

        Template = new FuncControlTemplate<UnderlineTabBar>((parent, _) =>
        {
            var itemsPresenter = new ItemsPresenter
            {
                Name = "PART_ItemsPresenter",
                [!ItemsPresenter.ItemsPanelProperty] = parent[!ItemsPanelProperty]
            };

            var indicator = new Border
            {
                Name = "PART_Indicator",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 3.0,
                CornerRadius = new CornerRadius(1.5),
                IsHitTestVisible = false,
                IsVisible = false
            };

            var rootPanel = new Panel
            {
                Name = "PART_Container",
                Background = Brushes.Transparent,
                Children = { itemsPresenter, indicator }
            };

            parent._animator?.Cancel();
            parent._indicator = indicator;
            parent._animator = new ElasticPillAnimator(indicator);
            parent.UpdateIndicatorBrush();

            return rootPanel;
        });

        Loaded += OnLoaded;
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not UnderlineTabItem;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new UnderlineTabItem();
    }

    protected override void PrepareContainerForItemOverride(Control element, object? item, int index)
    {
        base.PrepareContainerForItemOverride(element, item, index);
        if (element is UnderlineTabItem tabItem && item is not UnderlineTabItem)
        {
            tabItem.Value = item?.ToString();
            tabItem.Content = item;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
        ActualThemeVariantChanged += OnThemeVariantChanged;
        UpdateIndicatorBrush();
        SyncInitialSelection();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        LayoutUpdated -= OnLayoutUpdated;
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        _animator?.Cancel();
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateIndicatorBrush();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateIndicatorBrush();
        SyncInitialSelection();
    }

    public void UpdateIndicatorBrush()
    {
        if (_indicator == null) return;

        if (IndicatorBrush != null)
        {
            _indicator.Background = IndicatorBrush;
            return;
        }

        if (this.TryFindResource("AccentBrush", out var res) && res is IBrush b)
        {
            _indicator.Background = b;
        }
        else if (this.TryFindResource("ActiveChipBackground", out var chipBg) && chipBg is IBrush cb)
        {
            _indicator.Background = cb;
        }
        else
        {
            _indicator.Background = Brushes.White;
        }
    }

    public List<UnderlineTabItem> GetTabItems()
    {
        if (Presenter?.Panel != null)
        {
            var panelItems = Presenter.Panel.Children.OfType<UnderlineTabItem>().ToList();
            if (panelItems.Count > 0)
                return panelItems;
        }

        var directItems = Items.OfType<UnderlineTabItem>().ToList();
        if (directItems.Count > 0)
            return directItems;

        return LogicalChildren.OfType<UnderlineTabItem>().ToList();
    }

    private void OnSelectedValueChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_isUpdatingInternally) return;

        var newValue = e.GetNewValue<string?>();
        var items = GetTabItems();
        var targetIndex = -1;
        UnderlineTabItem? targetItem = null;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.Equals(item.Value, newValue, StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                targetItem = item;
                break;
            }
        }

        _isUpdatingInternally = true;
        try
        {
            SelectedIndex = targetIndex;
            UpdateTabsSelection(targetItem);
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        if (targetItem != null)
        {
            if (IsLoaded)
            {
                AnimateTo(targetItem);
            }
            else
            {
                SnapTo(targetItem);
            }
        }
    }

    private void OnSelectedIndexChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_isUpdatingInternally) return;

        var newIndex = e.GetNewValue<int>();
        var items = GetTabItems();
        UnderlineTabItem? targetItem = null;

        if (newIndex >= 0 && newIndex < items.Count)
        {
            targetItem = items[newIndex];
        }

        _isUpdatingInternally = true;
        try
        {
            SelectedValue = targetItem?.Value;
            UpdateTabsSelection(targetItem);
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        if (targetItem != null)
        {
            if (IsLoaded)
            {
                AnimateTo(targetItem);
            }
            else
            {
                SnapTo(targetItem);
            }
        }
    }

    internal void SelectTabItem(UnderlineTabItem item, bool animated)
    {
        var items = GetTabItems();
        var index = items.IndexOf(item);
        if (index < 0) return;

        _isUpdatingInternally = true;
        try
        {
            SelectedIndex = index;
            SelectedValue = item.Value;
            UpdateTabsSelection(item);
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        if (animated && IsLoaded)
        {
            AnimateTo(item);
        }
        else
        {
            SnapTo(item);
        }
    }

    private void UpdateTabsSelection(UnderlineTabItem? selectedItem)
    {
        var items = GetTabItems();
        foreach (var tab in items)
        {
            if (selectedItem != null)
            {
                tab.IsSelected = ReferenceEquals(tab, selectedItem);
            }
            else if (!string.IsNullOrEmpty(SelectedValue))
            {
                tab.IsSelected = string.Equals(tab.Value, SelectedValue, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                tab.IsSelected = false;
            }
        }
    }

    private void SyncInitialSelection()
    {
        var items = GetTabItems();
        if (items.Count == 0) return;

        UnderlineTabItem? selectedItem = null;

        if (SelectedValue != null)
        {
            selectedItem = items.FirstOrDefault(i => string.Equals(i.Value, SelectedValue, StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedIndex >= 0 && SelectedIndex < items.Count)
        {
            selectedItem = items[SelectedIndex];
        }
        else
        {
            selectedItem = items[0];
            SelectedValue = selectedItem.Value;
            SelectedIndex = 0;
        }

        if (selectedItem != null)
        {
            UpdateTabsSelection(selectedItem);
            SnapTo(selectedItem);
        }
    }

    public UnderlineTabItem? GetSelectedTabItem()
    {
        var items = GetTabItems();
        if (items.Count == 0) return null;

        if (SelectedValue != null)
        {
            var found = items.FirstOrDefault(i => string.Equals(i.Value, SelectedValue, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        if (SelectedIndex >= 0 && SelectedIndex < items.Count)
        {
            return items[SelectedIndex];
        }

        return items.FirstOrDefault();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_animator?.IsRunning == true) return;

        var selected = _lastSelectedTabItem ?? GetSelectedTabItem();
        if (selected != null && _indicator != null)
        {
            var container = _indicator.Parent as Visual ?? this;
            if (selected.TryGetIndicatorBounds(container, out double x, out double w))
            {
                if (!_indicator.IsVisible ||
                    Math.Abs(_indicator.Width - w) > 0.5 ||
                    Math.Abs(_indicator.Margin.Left - x) > 0.5)
                {
                    _indicator.Margin = new Thickness(x, 0, 0, 0);
                    _indicator.Width = w;
                    _indicator.IsVisible = true;
                }
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        if (_animator?.IsRunning != true && _indicator != null)
        {
            var selected = _lastSelectedTabItem ?? GetSelectedTabItem();
            if (selected != null)
            {
                var container = _indicator.Parent as Visual ?? this;
                if (selected.TryGetIndicatorBounds(container, out double x, out double w))
                {
                    if (!_indicator.IsVisible ||
                        Math.Abs(_indicator.Width - w) > 0.5 ||
                        Math.Abs(_indicator.Margin.Left - x) > 0.5)
                    {
                        _indicator.Margin = new Thickness(x, 0, 0, 0);
                        _indicator.Width = w;
                        _indicator.IsVisible = true;
                    }
                }
            }
        }

        return size;
    }

    /// <summary>
    /// Snaps the underline indicator immediately to the target value without animation.
    /// Ideal for when the parent window is opened or restored.
    /// </summary>
    public void SnapTo(string? value)
    {
        var items = GetTabItems();
        var target = items.FirstOrDefault(i => string.Equals(i.Value, value, StringComparison.OrdinalIgnoreCase))
                     ?? items.FirstOrDefault();

        if (target != null)
        {
            _isUpdatingInternally = true;
            try
            {
                SelectedValue = target.Value;
                SelectedIndex = items.IndexOf(target);
                UpdateTabsSelection(target);
            }
            finally
            {
                _isUpdatingInternally = false;
            }
            SnapTo(target);
        }
    }

    /// <summary>
    /// Snaps the underline indicator immediately to the specified item without animation.
    /// </summary>
    public void SnapTo(UnderlineTabItem target)
    {
        _animator?.Cancel();
        _lastSelectedTabItem = target;

        if (_indicator == null) return;

        var container = _indicator.Parent as Visual ?? this;
        if (target.TryGetIndicatorBounds(container, out double x, out double w))
        {
            _indicator.Margin = new Thickness(x, 0, 0, 0);
            _indicator.Width = w;
            _indicator.IsVisible = true;
        }
    }

    /// <summary>
    /// Animates the underline indicator to the target item using two-phase elastic transition.
    /// </summary>
    public void AnimateTo(UnderlineTabItem target)
    {
        if (_animator == null || _indicator == null)
        {
            SnapTo(target);
            return;
        }

        var prev = _lastSelectedTabItem;
        _lastSelectedTabItem = target;

        if (prev == null || ReferenceEquals(prev, target) || !prev.IsEffectivelyVisible)
        {
            SnapTo(target);
            return;
        }

        var container = _indicator.Parent as Visual ?? this;
        if (prev.TryGetIndicatorBounds(container, out double fromX, out double fromW) &&
            target.TryGetIndicatorBounds(container, out double toX, out double toW))
        {
            _animator.Animate(fromX, fromW, toX, toW);
        }
        else
        {
            SnapTo(target);
        }
    }

    /// <summary>
    /// Navigates to the next or previous tab item (supporting horizontal arrow keys navigation).
    /// By default (wrapAround = false), navigation stops at the boundaries and does not cycle/loop.
    /// </summary>
    public void Cycle(bool forward, bool wrapAround = false)
    {
        var items = GetTabItems();
        if (items.Count == 0) return;

        var currentIndex = SelectedIndex;
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }
        else
        {
            if (forward)
            {
                currentIndex = wrapAround
                    ? (currentIndex + 1) % items.Count
                    : Math.Min(currentIndex + 1, items.Count - 1);
            }
            else
            {
                currentIndex = wrapAround
                    ? (currentIndex - 1 + items.Count) % items.Count
                    : Math.Max(currentIndex - 1, 0);
            }
        }

        if (currentIndex != SelectedIndex)
        {
            SelectTabItem(items[currentIndex], animated: true);
        }
    }

    /// <summary>
    /// Handles clicks anywhere on the tab bar (e.g. gaps between items or padding area).
    /// If an item did not handle the click directly, intelligently selects the horizontally closest tab item.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var pt = e.GetPosition(this);
            var items = GetTabItems();
            if (items.Count == 0) return;

            UnderlineTabItem? closestTab = null;
            double minDistance = double.MaxValue;

            foreach (var tab in items)
            {
                if (!tab.IsVisible || tab.Bounds.Width <= 0) continue;

                var tabPt = tab.TranslatePoint(new Point(0, 0), this);
                double tabLeft = tabPt?.X ?? tab.Bounds.X;
                double tabTop = tabPt?.Y ?? tab.Bounds.Y;
                double tabBottom = tabTop + tab.Bounds.Height;

                // Allow comfortable vertical tolerance within the tab bar area
                if (pt.Y < tabTop - 12.0 || pt.Y > tabBottom + 12.0) continue;

                double tabCenterX = tabLeft + tab.Bounds.Width / 2.0;
                double distance = Math.Abs(pt.X - tabCenterX);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestTab = tab;
                }
            }

            if (closestTab != null)
            {
                SelectTabItem(closestTab, animated: true);
                e.Handled = true;
            }
        }
    }
}
