using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SlimClip.UI.ViewModels;

namespace SlimClip.UI.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        Avalonia.Media.TextOptions.SetTextRenderingMode(this, Avalonia.Media.TextRenderingMode.SubpixelAntialias);

        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }
    }

    public SetupWindow(SetupViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }

    private void OnInstallPathLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm && vm.IsPathEditable)
        {
            vm.ApplySelectedPath(vm.InstallPath);
        }
    }

    private async void OnBrowseInstallPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel vm || !vm.IsPathEditable)
            return;

        var options = new FolderPickerOpenOptions
        {
            Title = vm.L["Setup_Path_PickerTitle"],
            AllowMultiple = false
        };

        try
        {
            var initialDir = vm.InstallPath;
            if (!Directory.Exists(initialDir))
            {
                initialDir = Path.GetDirectoryName(initialDir);
            }
            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
            {
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(initialDir);
            }
        }
        catch { }

        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            var chosen = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                vm.ApplySelectedPath(chosen);
            }
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel { IsInstallingStage: true })
            return;

        Close();
    }
}
