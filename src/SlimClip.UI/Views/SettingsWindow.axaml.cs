using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SlimClip.Core.Models;
using SlimClip.UI.Animation;
using SlimClip.UI.Converters;
using SlimClip.UI.ViewModels;

namespace SlimClip.UI.Views;

public partial class SettingsWindow : Window
{
    private DispatcherTimer? _spinnerTimer;
    private double _spinnerAngle;
    private readonly Dictionary<ScrollBar, DispatcherTimer> _scrollTimers = new();

    public SettingsWindow()
    {
        InitializeComponent();
        Avalonia.Media.RenderOptions.SetTextRenderingMode(this, Avalonia.Media.TextRenderingMode.SubpixelAntialias);

        var tabBar = this.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("SettingsTabBar");
        if (tabBar != null)
        {
            tabBar.PropertyChanged += (s, e) =>
            {
                if (e.Property == SlimClip.UI.Controls.UnderlineTabBar.SelectedValueProperty)
                {
                    SwitchTab(tabBar.SelectedValue);
                }
            };
        }
        SwitchTab("General");



        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }

        SetupScrollAutoHiding();

        KeyDown += OnWindowKeyDown;
        Deactivated += (s, e) =>
        {
            if (DataContext is SettingsViewModel vm && vm.IsRecordingHotkey)
            {
                vm.StopRecordingHotkey();
            }
        };
        Closed += (s, e) =>
        {
            _spinnerTimer?.Stop();
            foreach (var timer in _scrollTimers.Values)
            {
                timer.Stop();
            }
            _scrollTimers.Clear();
            if (DataContext is SettingsViewModel vm && vm.IsRecordingHotkey)
            {
                vm.StopRecordingHotkey();
            }
        };
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;

        switch (viewModel.ThemeMode)
        {
            case AppThemeMode.Light:
                ThemeLightRadio.IsChecked = true;
                break;
            case AppThemeMode.Dark:
                ThemeDarkRadio.IsChecked = true;
                break;
            default:
                ThemeSystemRadio.IsChecked = true;
                break;
        }

        viewModel.CopyToClipboardRequested += async text =>
        {
            if (Clipboard != null)
            {
                await Clipboard.SetTextAsync(text);
            }
        };
    }

    public void SelectTab(string tabName)
    {
        var tabBar = this.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("SettingsTabBar");
        if (tabBar != null)
        {
            tabBar.SelectedValue = tabName;
        }
    }

    private void SwitchTab(string? tabValue)
    {
        var panelGeneral = this.FindControl<ScrollViewer>("PanelGeneral");
        var panelAppearance = this.FindControl<ScrollViewer>("PanelAppearance");
        var panelHotkey = this.FindControl<ScrollViewer>("PanelHotkey");
        var panelData = this.FindControl<ScrollViewer>("PanelData");
        var panelAdvanced = this.FindControl<ScrollViewer>("PanelAdvanced");
        var panelAbout = this.FindControl<ScrollViewer>("PanelAbout");

        if (panelGeneral != null) panelGeneral.IsVisible = tabValue == "General";
        if (panelAppearance != null) panelAppearance.IsVisible = tabValue == "Appearance";
        if (panelHotkey != null) panelHotkey.IsVisible = tabValue == "Hotkey";
        if (panelData != null) panelData.IsVisible = tabValue == "Data";
        if (panelAdvanced != null) panelAdvanced.IsVisible = tabValue == "Advanced";
        if (panelAbout != null) panelAbout.IsVisible = tabValue == "About";

        foreach (var (bar, timer) in _scrollTimers)
        {
            timer.Stop();
            bar.Classes.Set("scrolling", false);
        }
    }

    private void OnThemeSystemChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ThemeMode = AppThemeMode.System;
    }

    private void OnThemeLightChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ThemeMode = AppThemeMode.Light;
    }

    private void OnThemeDarkChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ThemeMode = AppThemeMode.Dark;
    }

    private void OnClearAllDataClicked(object? sender, RoutedEventArgs e)
    {
        var overlay = this.FindControl<Border>("ClearDataOverlay");
        var confirmPanel = this.FindControl<StackPanel>("ClearConfirmPanel");
        var progressPanel = this.FindControl<StackPanel>("ClearProgressPanel");
        var successPanel = this.FindControl<StackPanel>("ClearSuccessPanel");

        if (confirmPanel != null) confirmPanel.IsVisible = true;
        if (progressPanel != null) progressPanel.IsVisible = false;
        if (successPanel != null) successPanel.IsVisible = false;
        if (overlay != null) overlay.IsVisible = true;
    }

    private void OnCancelClearClicked(object? sender, RoutedEventArgs e)
    {
        var overlay = this.FindControl<Border>("ClearDataOverlay");
        if (overlay != null) overlay.IsVisible = false;
    }

    private async void OnConfirmClearClicked(object? sender, RoutedEventArgs e)
    {
        var overlay = this.FindControl<Border>("ClearDataOverlay");
        var confirmPanel = this.FindControl<StackPanel>("ClearConfirmPanel");
        var progressPanel = this.FindControl<StackPanel>("ClearProgressPanel");
        var successPanel = this.FindControl<StackPanel>("ClearSuccessPanel");
        var spinnerArc = this.FindControl<Avalonia.Controls.Shapes.Arc>("ClearSpinnerArc");

        if (confirmPanel != null) confirmPanel.IsVisible = false;
        if (progressPanel != null) progressPanel.IsVisible = true;

        _spinnerAngle = 0;
        _spinnerTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, ev) =>
        {
            _spinnerAngle = (_spinnerAngle + 8) % 360;
            if (spinnerArc != null)
            {
                spinnerArc.RenderTransform = new RotateTransform(_spinnerAngle);
            }
        });
        _spinnerTimer.Start();

        try
        {
            // Execute clear task and ensure at least 1000ms delay so user can clearly perceive the action
            Task cleanTask = Task.CompletedTask;
            if (DataContext is SettingsViewModel vm)
            {
                cleanTask = vm.ClearAllDataAsync();
            }
            var minDelayTask = Task.Delay(1000);

            await Task.WhenAll(cleanTask, minDelayTask);
        }
        finally
        {
            _spinnerTimer.Stop();
        }

        if (progressPanel != null) progressPanel.IsVisible = false;
        if (successPanel != null) successPanel.IsVisible = true;

        // Display checkmark success state for 1200ms before auto-dismissing
        await Task.Delay(1200);

        if (overlay != null) overlay.IsVisible = false;
        if (confirmPanel != null) confirmPanel.IsVisible = true;
        if (successPanel != null) successPanel.IsVisible = false;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.IsRecordingHotkey)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                vm.StopRecordingHotkey();
                return;
            }

            if (e.Key == Key.Back)
            {
                e.Handled = true;
                vm.ClearHotkey();
                return;
            }

            // On Windows, WindowsHotkeyService's low-level keyboard hook manages
            // key recording to accurately capture Win key chords and neutralize OS shell intercept.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Ignore isolated modifier keys
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }

            var isFunctionKey = e.Key >= Key.F1 && e.Key <= Key.F24;
            var mods = SlimClip.Core.Models.KeyModifiers.None;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
                mods |= SlimClip.Core.Models.KeyModifiers.Alt;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
                mods |= SlimClip.Core.Models.KeyModifiers.Control;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                mods |= SlimClip.Core.Models.KeyModifiers.Shift;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))
                mods |= SlimClip.Core.Models.KeyModifiers.WindowsOrCommand;

            if (mods == SlimClip.Core.Models.KeyModifiers.None && !isFunctionKey)
            {
                return;
            }

            // A valid global shortcut must contain at least Cmd, Ctrl, or Alt (or be a function key).
            // Prevent accidentally registering bare Shift + Key which would swallow normal typing.
            if (!isFunctionKey &&
                !mods.HasFlag(SlimClip.Core.Models.KeyModifiers.WindowsOrCommand) &&
                !mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Control) &&
                !mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Alt))
            {
                return;
            }

            var keyCode = KeyMapper.AvaloniaKeyToPlatformCode(e.Key);
            var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            string display;
            if (isMac)
            {
                var parts = "";
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Control)) parts += "⌃";
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Alt)) parts += "⌥";
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Shift)) parts += "⇧";
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.WindowsOrCommand)) parts += "⌘";
                parts += e.Key.ToString();
                display = parts;
            }
            else
            {
                var parts = new System.Collections.Generic.List<string>();
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.WindowsOrCommand)) parts.Add("Win");
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Control)) parts.Add("Ctrl");
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Alt)) parts.Add("Alt");
                if (mods.HasFlag(SlimClip.Core.Models.KeyModifiers.Shift)) parts.Add("Shift");
                parts.Add(e.Key.ToString());
                display = string.Join(" + ", parts);
            }

            e.Handled = true;
            vm.ApplyRecordedHotkey(mods, keyCode, display);
            return;
        }

        if (e.Key == Key.Escape)
        {
            var overlay = this.FindControl<Border>("ClearDataOverlay");
            if (overlay != null && overlay.IsVisible)
            {
                overlay.IsVisible = false;
                e.Handled = true;
                return;
            }

            e.Handled = true;
            Close();
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetupScrollAutoHiding()
    {
        var panelNames = new[]
        {
            "PanelGeneral", "PanelAppearance", "PanelHotkey",
            "PanelData", "PanelAdvanced", "PanelAbout"
        };

        foreach (var name in panelNames)
        {
            var sv = this.FindControl<ScrollViewer>(name);
            if (sv == null) continue;

            sv.ScrollChanged += (s, e) =>
            {
                if (Math.Abs(e.OffsetDelta.Y) > 0.001)
                {
                    OnScrollViewerScrolled(sv);
                }
            };
        }
    }

    private void OnScrollViewerScrolled(ScrollViewer sv)
    {
        var vBar = sv.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(b => b.Orientation == Orientation.Vertical);
        if (vBar == null) return;

        vBar.Classes.Set("scrolling", true);

        if (!_scrollTimers.TryGetValue(vBar, out var timer))
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                vBar.Classes.Set("scrolling", false);
            };
            _scrollTimers[vBar] = timer;
        }

        timer.Stop();
        timer.Start();
    }
}
