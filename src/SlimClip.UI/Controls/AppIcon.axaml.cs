using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace SlimClip.UI.Controls;

public partial class AppIcon : UserControl
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<AppIcon, string>(nameof(Kind), "Search");

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<AppIcon, double>(nameof(Size), 16.0);

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public AppIcon()
    {
        InitializeComponent();
        Width = Size;
        Height = Size;
        UpdateIconGeometry();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            UpdateIconGeometry();
        }
        else if (change.Property == SizeProperty)
        {
            var newSize = change.GetNewValue<double>();
            Width = newSize;
            Height = newSize;
        }
    }

    private void UpdateIconGeometry()
    {
        var path = this.FindControl<Avalonia.Controls.Shapes.Path>("IconPath");
        if (path == null)
            return;

        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var geometryData = isMac ? GetMacSymbolPath(Kind) : GetFluentIconPath(Kind);

        if (!string.IsNullOrEmpty(geometryData))
        {
            try
            {
                path.Data = StreamGeometry.Parse(geometryData);
            }
            catch { }
        }
    }

    // Windows Fluent Icons geometries
    private static string GetFluentIconPath(string kind) => kind.ToLowerInvariant() switch
    {
        "pin" => "M16,9V4L17,2H7L8,4V9C8,10.1 7.1,11 6,11V13H11V22H13V13H18V11C16.9,11 16,10.1 16,9Z",
        "star" or "favorite" => "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z",
        "delete" => "M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19M8,9H16V19H8V9M15.5,4L14.5,3H9.5L8.5,4H5V6H19V4H15.5Z",
        "settings" => "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z",
        "close" => "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
        "search" => "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z",
        "text" or "texttype" => "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M8,12V14H16V12H8M8,16V18H13V16H8Z",
        "image" or "imagetype" => "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z",
        "file" or "filetype" => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
        "warning" => "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16",
        "check" => "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z",
        "copy" => "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z",
        "edit" => "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z",
        _ => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z"
    };

    // macOS SF Symbols geometries
    private static string GetMacSymbolPath(string kind) => kind.ToLowerInvariant() switch
    {
        "pin" => "M15.5,3.5C15.5,3.22 15.28,3 15,3H9C8.72,3 8.5,3.22 8.5,3.5V9.29L6.15,11.65C6.05,11.74 6,11.87 6,12V13C6,13.28 6.22,13.5 6.5,13.5H11.25V20.5C11.25,20.78 11.47,21 11.75,21H12.25C12.53,21 12.75,20.78 12.75,20.5V13.5H17.5C17.78,13.5 18,13.28 18,13V12C18,11.87 17.95,11.74 17.85,11.65L15.5,9.29V3.5Z",
        "star" or "favorite" => "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z",
        "delete" => "M9,3H15A1,1 0 0,1 16,4V5H20A0.5,0.5 0 0,1 20.5,5.5V6A0.5,0.5 0 0,1 20,6.5H4A0.5,0.5 0 0,1 3.5,6V5.5A0.5,0.5 0 0,1 4,5H8V4A1,1 0 0,1 9,3M5.5,8H18.5L17.5,19.5A2,2 0 0,1 15.5,21.5H8.5A2,2 0 0,1 6.5,19.5L5.5,8Z",
        "settings" => "M12,8.5A3.5,3.5 0 1,0 15.5,12A3.5,3.5 0 0,0 12,8.5ZM19.2,11.1L20.8,9.7A0.5,0.5 0 0,0 20.9,9L19.4,6.4A0.5,0.5 0 0,0 18.8,6.2L16.9,7A7.5,7.5 0 0,0 15.3,6.1L15,4.1A0.5,0.5 0 0,0 14.5,3.7H11.5A0.5,0.5 0 0,0 11,4.1L10.7,6.1A7.5,7.5 0 0,0 9.1,7L7.2,6.2A0.5,0.5 0 0,0 6.6,6.4L5.1,9A0.5,0.5 0 0,0 5.2,9.7L6.8,11.1A7.8,7.8 0 0,0 6.8,12.9L5.2,14.3A0.5,0.5 0 0,0 5.1,15L6.6,17.6A0.5,0.5 0 0,0 7.2,17.8L9.1,17A7.5,7.5 0 0,0 10.7,17.9L11,19.9A0.5,0.5 0 0,0 11.5,20.3H14.5A0.5,0.5 0 0,0 15,19.9L15.3,17.9A7.5,7.5 0 0,0 16.9,17L18.8,17.8A0.5,0.5 0 0,0 19.4,17.6L20.9,15A0.5,0.5 0 0,0 20.8,14.3L19.2,12.9A7.8,7.8 0 0,0 19.2,11.1Z",
        "close" => "M18.3,5.71C17.91,5.32 17.28,5.32 16.89,5.71L12,10.59L7.11,5.7C6.72,5.31 6.09,5.31 5.7,5.7C5.31,6.09 5.31,6.72 5.7,7.11L10.59,12L5.7,16.89C5.31,17.28 5.31,17.91 5.7,18.3C6.09,18.69 6.72,18.69 7.11,18.3L12,13.41L16.89,18.3C17.28,18.69 17.91,18.69 18.3,18.3C18.69,17.91 18.69,17.28 18.3,16.89L13.41,12L18.3,7.11C18.68,6.73 18.68,6.09 18.3,5.71Z",
        "search" => "M10,2A8,8 0 0,1 18,10C18,11.85 17.37,13.55 16.31,14.9L21.71,20.29A1,1 0 0,1 20.29,21.71L14.9,16.31C13.55,17.37 11.85,18 10,18A8,8 0 0,1 2,10A8,8 0 0,1 10,2M10,4A6,6 0 0,0 4,10A6,6 0 0,0 10,16A6,6 0 0,0 16,10A6,6 0 0,0 10,4Z",
        "text" or "texttype" => "M14.5,2H7A3,3 0 0,0 4,5V19A3,3 0 0,0 7,22H17A3,3 0 0,0 20,19V7.5L14.5,2M14,3.5L18.5,8H14V3.5M8,12H16V13.5H8V12M8,15.5H16V17H8V15.5Z",
        "image" or "imagetype" => "M5,4H19A3,3 0 0,1 22,7V17A3,3 0 0,1 19,20H5A3,3 0 0,1 2,17V7A3,3 0 0,1 5,4M5,5.5A1.5,1.5 0 0,0 3.5,7V17A1.5,1.5 0 0,0 5,18.5H19A1.5,1.5 0 0,0 20.5,17V7A1.5,1.5 0 0,0 19,5.5H5M7.5,7.5A1.5,1.5 0 1,1 6,9A1.5,1.5 0 0,1 7.5,7.5M6,16L10,11L13,15L15.5,12L18,16H6Z",
        "file" or "filetype" => "M4,4H9.17L11.17,6H20A2,2 0 0,1 22,8V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,7.5V18H20V8H10.83L8.83,6H4V7.5Z",
        "warning" => "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16",
        "check" => "M20.3,5.7A1,1 0 0,0 18.9,5.7L9,15.6L5.1,11.7A1,1 0 0,0 3.7,13.1L8.3,17.7A1,1 0 0,0 9.7,17.7L20.3,7.1A1,1 0 0,0 20.3,5.7Z",
        "copy" => "M16,1H4C2.9,1 2,1.9 2,3V17H4V3H16V1M19,5H8C6.9,5 6,5.9 6,7V21C6,22.1 6.9,23 8,23H19C20.1,23 21,22.1 21,21V7C21,5.9 20.1,5 19,5M19,21H8V7H19V21Z",
        "edit" => "M3,17.25V21H6.75L17.81,9.94L14.06,6.19L3,17.25M20.71,7.04C21.1,6.65 21.1,6.02 20.71,5.63L18.37,3.29C17.98,2.9 17.35,2.9 16.96,3.29L15.13,5.12L18.88,8.87L20.71,7.04Z",
        _ => "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2Z"
    };
}
