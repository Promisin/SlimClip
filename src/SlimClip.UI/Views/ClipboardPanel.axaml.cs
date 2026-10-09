using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Animation;
using SlimClip.UI.ViewModels;

namespace SlimClip.UI.Views;

public partial class ClipboardPanel : Window
{
    private readonly Action? _openSettingsAction;
    private readonly IWindowActivationService? _windowActivationService;
    private bool _isClosing;
    private bool _isSettingsOpened;

    public IntPtr PreviousForegroundHandle { get; set; } = IntPtr.Zero;

    public void Dismiss()
    {
        _isClosing = true;
        Hide();
        if (_windowActivationService != null && PreviousForegroundHandle != IntPtr.Zero)
        {
            _windowActivationService.RestoreForegroundWindow(PreviousForegroundHandle);
            PreviousForegroundHandle = IntPtr.Zero;
        }
    }

    public ClipboardPanel()
    {
        InitializeComponent();
        Avalonia.Media.RenderOptions.SetTextRenderingMode(this, Avalonia.Media.TextRenderingMode.SubpixelAntialias);

        var tabBar = this.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("FilterTabBar");
        if (tabBar != null && string.IsNullOrEmpty(tabBar.SelectedValue))
        {
            tabBar.SelectedValue = "All";
        }

        ConfigurePlatformKeyHints();
    }

    public ClipboardPanel(MainViewModel viewModel, Action? openSettingsAction = null, IWindowActivationService? windowActivationService = null) : this()
    {
        DataContext = viewModel;
        _openSettingsAction = openSettingsAction;
        _windowActivationService = windowActivationService;

        viewModel.ConfirmMissingFiles += OnConfirmMissingFiles;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        var header = this.FindControl<Border>("HeaderBorder");
        if (header != null)
        {
            header.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }

        Deactivated += (s, e) =>
        {
            if (!_isClosing && !_isSettingsOpened)
            {
                _isClosing = true;
                Hide();
                PreviousForegroundHandle = IntPtr.Zero;
            }
        };

        Activated += (s, e) =>
        {
            _isClosing = false;
            FocusSearchBox();
        };

        KeyDown += OnWindowKeyDown;
    }

    private void ConfigurePlatformKeyHints()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var categoryHint = this.FindControl<TextBlock>("CategoryKeyHint");
            var navHint = this.FindControl<TextBlock>("NavigateKeyHint");
            var pasteHint = this.FindControl<TextBlock>("PasteKeyHint");
            var closeHint = this.FindControl<TextBlock>("CloseKeyHint");

            if (categoryHint != null) categoryHint.Text = "◀ ▶";
            if (navHint != null) navHint.Text = "▲ ▼";
            if (pasteHint != null) pasteHint.Text = "↩";
            if (closeHint != null) closeHint.Text = "⎋";
        }
    }

    public void ShowPanel()
    {
        _isClosing = false;
        _isSettingsOpened = false;

        if (DataContext is MainViewModel vm)
        {
            vm.ResetState();
            if (vm.SelectedItem != null)
            {
                var listBox = this.FindControl<ListBox>("ItemsListBox");
                listBox?.ScrollIntoView(vm.SelectedItem);
            }
        }

        Show();
        Activate();

        var tabBar = this.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("FilterTabBar");
        tabBar?.SnapTo("All");

        if (_windowActivationService != null && TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            _windowActivationService.ActivateWindow(hwnd);
        }

        FocusSearchBox();
    }

    public void FocusSearchBox()
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null)
        {
            searchBox.Focus();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                searchBox.Focus();
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel editingVm && editingVm.IsEditingItem)
        {
            var isCtrlOrCmd = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);
            if (isCtrlOrCmd && e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = editingVm.SaveEditItemCommand.ExecuteAsync(null);
                return;
            }
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                editingVm.CancelEditItemCommand.Execute(null);
                return;
            }
            return;
        }

        var isModifierDown = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);
        if (isModifierDown && e.Key == Key.Back)
        {
            if (DataContext is MainViewModel vm && vm.SelectedItem != null)
            {
                e.Handled = true;
                _ = vm.DeleteItemCommand.ExecuteAsync(vm.SelectedItem);
                return;
            }
        }

        if (isModifierDown && e.Key == Key.D)
        {
            if (DataContext is MainViewModel vm && vm.SelectedItem != null)
            {
                e.Handled = true;
                _ = vm.ToggleFavoriteCommand.ExecuteAsync(vm.SelectedItem);
                return;
            }
        }

        if (isModifierDown && e.Key == Key.E)
        {
            if (DataContext is MainViewModel vm && vm.SelectedItem != null && vm.SelectedItem.IsText)
            {
                e.Handled = true;
                vm.StartEditItemCommand.Execute(vm.SelectedItem);
                return;
            }
        }

        if (isModifierDown && e.Key == Key.C)
        {
            if (DataContext is MainViewModel vm && vm.SelectedItem != null)
            {
                var searchBox = this.FindControl<TextBox>("SearchBox");
                if (searchBox == null || !searchBox.IsFocused || string.IsNullOrEmpty(searchBox.SelectedText))
                {
                    e.Handled = true;
                    _ = vm.CopyItemToClipboardCommand.ExecuteAsync(vm.SelectedItem);
                    return;
                }
            }
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                ExecutePaste();
                break;

            case Key.Escape:
                e.Handled = true;
                Dismiss();
                break;

            case Key.Left:
                if (ShouldHandleHorizontalFilter())
                {
                    e.Handled = true;
                    CycleFilter(false);
                }
                break;

            case Key.Right:
                if (ShouldHandleHorizontalFilter())
                {
                    e.Handled = true;
                    CycleFilter(true);
                }
                break;

            case Key.Up:
            case Key.Down:
                // Move selection in listbox
                var listBox = this.FindControl<ListBox>("ItemsListBox");
                if (listBox != null && DataContext is MainViewModel vm && vm.Items.Count > 0)
                {
                    var currentIndex = vm.SelectedItem != null ? vm.Items.IndexOf(vm.SelectedItem) : -1;
                    if (e.Key == Key.Down)
                    {
                        var next = Math.Min(currentIndex + 1, vm.Items.Count - 1);
                        vm.SelectedItem = vm.Items[next];
                        listBox.ScrollIntoView(vm.SelectedItem);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Up)
                    {
                        var prev = Math.Max(currentIndex - 1, 0);
                        vm.SelectedItem = vm.Items[prev];
                        listBox.ScrollIntoView(vm.SelectedItem);
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    private bool ShouldHandleHorizontalFilter()
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null && searchBox.IsFocused)
        {
            return string.IsNullOrEmpty(searchBox.Text);
        }
        return true;
    }

    private void CycleFilter(bool forward)
    {
        var tabBar = this.FindControl<SlimClip.UI.Controls.UnderlineTabBar>("FilterTabBar");
        if (tabBar != null)
        {
            tabBar.Cycle(forward);
        }
        else if (DataContext is MainViewModel vm)
        {
            vm.CycleFilter(forward);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        ExecutePaste();
    }

    private async void ExecutePaste()
    {
        if (DataContext is MainViewModel vm && vm.SelectedItem != null)
        {
            _isClosing = true;
            PreviousForegroundHandle = IntPtr.Zero;
            Hide();
            await vm.PasteSelectedCommand.ExecuteAsync(null);
        }
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        _isClosing = true;
        _isSettingsOpened = true;
        PreviousForegroundHandle = IntPtr.Zero;
        Hide();
        _openSettingsAction?.Invoke();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private Task<bool> OnConfirmMissingFiles(string message)
    {
        // For headless or quick confirmation, default to true or could show dialog
        return Task.FromResult(true);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsEditingItem) && DataContext is MainViewModel vm)
        {
            if (vm.IsEditingItem)
            {
                var textBox = this.FindControl<TextBox>("EditItemTextBox");
                if (textBox != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        textBox.Focus();
                        if (textBox.Text != null)
                        {
                            textBox.CaretIndex = textBox.Text.Length;
                        }
                    }, Avalonia.Threading.DispatcherPriority.Input);
                }
            }
            else
            {
                FocusSearchBox();
            }
        }
    }

    private void OnOverlayBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainViewModel vm && vm.IsEditingItem)
        {
            vm.CancelEditItemCommand.Execute(null);
        }
    }
}
