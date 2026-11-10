using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.Platform.MacOS;
using SlimClip.UI.Helpers;
using SlimClip.UI.ViewModels;
using SlimClip.UI.Views;

#if WINDOWS
using SlimClip.Platform.Windows;
#endif

namespace SlimClip.Desktop;

public partial class App : Application
{
    private ISettingsService _settingsService = null!;
    private ILocalizationService _localizationService = null!;
    private IClipboardHistory _clipboardHistory = null!;
    private IClipboardMonitor _clipboardMonitor = null!;
    private IPasteService _pasteService = null!;
    private IWindowAnchorService _windowAnchorService = null!;
    private IHotkeyService _hotkeyService = null!;
    private IWindowActivationService _windowActivationService = null!;

    private MainViewModel _mainViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;

    private ClipboardPanel? _clipboardPanel;
    private SettingsWindow? _settingsWindow;
    private ResourceDictionary? _solidBackdropDict;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showMenuItem;
    private NativeMenuItem? _settingsMenuItem;
    private NativeMenuItem? _checkUpdateMenuItem;
    private NativeMenuItem? _exitMenuItem;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Dynamically load platform theme design tokens
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var themeUri = new Uri(isMac
            ? "avares://SlimClip.UI/Themes/MacHIGTheme.axaml"
            : "avares://SlimClip.UI/Themes/Fluent2Theme.axaml");

        try
        {
            var themeDict = (ResourceDictionary)AvaloniaXamlLoader.Load(themeUri);
            Resources.MergedDictionaries.Add(themeDict);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load design tokens: {ex.Message}");
        }
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryStartWindowsSetupMode(desktop))
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }
#endif

            var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // Configure macOS Accessory App Policy (LSUIElement = true / hide from dock)
            if (isMac)
            {
                ConfigureMacAccessoryApp();
            }

            // 1. DI and Services Instantiation
            IPlatformSettingsHook? platformSettingsHook = null;
#if WINDOWS
            if (isWin)
            {
                platformSettingsHook = new WindowsSettingsPlatformService();
            }
#endif
            if (isMac)
            {
                platformSettingsHook = new MacSettingsPlatformService();
            }

            _settingsService = new SettingsService(platformSettingsHook);
            _localizationService = new LocalizationService(_settingsService.Settings.Language);

            var dataDir = _settingsService.GetDataDirectory();
            var dbPath = Path.Combine(dataDir, "slimclip.db");
            var imagesDir = Path.Combine(dataDir, "images");

            _clipboardHistory = new ClipboardHistory(dbPath, imagesDir);
            await _clipboardHistory.InitializeAsync();

#if WINDOWS
            if (isWin)
            {
                _clipboardMonitor = new WindowsClipboardMonitor();
                _pasteService = new WindowsPasteService();
                _windowAnchorService = new WindowsWindowAnchorService();
                _hotkeyService = new WindowsHotkeyService();
                _windowActivationService = new WindowsWindowActivationService();
            }
            else
#endif
            {
                _clipboardMonitor = new MacClipboardMonitor();
                _pasteService = new MacPasteService();
                _windowAnchorService = new MacWindowAnchorService();
                _hotkeyService = new MacHotkeyService();
                _windowActivationService = new MacWindowActivationService();
            }

            // 2. ViewModels
            _mainViewModel = new MainViewModel(
                _clipboardHistory,
                _clipboardMonitor,
                _pasteService,
                _settingsService,
                _localizationService)
            {
                SearchDebounceMs = 250
            };

            _settingsViewModel = new SettingsViewModel(
                _settingsService,
                _clipboardHistory,
                _hotkeyService,
                _localizationService);

            await _mainViewModel.InitializeAsync();

            // 3. Views
            _clipboardPanel = new ClipboardPanel(_mainViewModel, OpenSettingsWindow, _windowActivationService);
            AttachPlatformBackdrop(_clipboardPanel);
            var appIcon = LoadAppIcon();
            if (appIcon != null)
            {
                _clipboardPanel.Icon = appIcon;
            }
            _localizationService.LanguageChanged += (s, lang) => UpdateTrayMenuLanguage();
            _settingsViewModel.HotkeyChanged += (s, cfg) =>
            {
                _hotkeyService.Register(cfg);
                UpdateTrayMenuHotkey(cfg);
            };
            _settingsViewModel.ThemeModeChanged += (s, mode) => ApplyThemeMode(mode);
            _settingsViewModel.BackdropStyleChanged += (s, style) => ApplyBackdropStyle(style);
            _settingsViewModel.DataCleared += async (s, e) =>
            {
                await _mainViewModel.InitializeAsync();
            };

            ActualThemeVariantChanged += (s, e) =>
            {
                if (_settingsService?.Settings.ThemeMode == AppThemeMode.System)
                {
                    ApplyThemeMode(AppThemeMode.System);
                }
            };

            ApplyBackdropStyle(_settingsService.Settings.BackdropStyle);
            ApplyThemeMode(_settingsService.Settings.ThemeMode);

            // 4. Hotkey Setup
            _hotkeyService.HotkeyPressed += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(ShowClipboardPanelNearAnchor);
            };
            _hotkeyService.Register(_settingsService.Settings.Hotkey);
            _settingsViewModel.RefreshHotkeyRegistrationStatus();

            // 5. Tray Icon / Status Item
            SetupTrayIcon();

            // 6. Start Clipboard Monitoring
            _clipboardMonitor.Start();

            // 7. Pre-warm clipboard panel handle silently to eliminate first-show cold start and focus jitter
            PreWarmClipboardPanel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void PreWarmClipboardPanel()
    {
        if (_clipboardPanel == null) return;
        try
        {
            _clipboardPanel.Opacity = 0;
            _clipboardPanel.Show();
            _clipboardPanel.Hide();
            _clipboardPanel.Opacity = 1;
            MemoryTrimHelper.ScheduleBackgroundTrim(_mainViewModel);
        }
        catch { }
    }

    public void ShowClipboardPanelNearAnchor()
    {
        if (_clipboardPanel == null)
            return;

        if (_clipboardPanel.IsVisible)
        {
            _clipboardPanel.Dismiss();
            return;
        }

        var currentForeground = _windowActivationService.GetForegroundWindowHandle();
        var panelHwnd = _clipboardPanel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (currentForeground != IntPtr.Zero && currentForeground != panelHwnd)
        {
#if WINDOWS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _pasteService is WindowsPasteService winPaste)
            {
                winPaste.TargetWindowHandle = currentForeground;
            }
#endif
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && _pasteService is MacPasteService macPaste)
            {
                macPaste.TargetApplication = currentForeground;
            }
            _clipboardPanel.PreviousForegroundHandle = currentForeground;
        }
        else
        {
            _clipboardPanel.PreviousForegroundHandle = IntPtr.Zero;
        }

        var anchor = _windowAnchorService.GetActivationAnchor();
        var (x, y) = _windowAnchorService.CalculateWindowPosition(
            anchor,
            (int)_clipboardPanel.Width,
            (int)_clipboardPanel.Height,
            0, 0, 0, 0);

        _clipboardPanel.Position = new PixelPoint(x, y);
        _clipboardPanel.ShowPanel();
    }

    public void OpenSettingsWindow() => OpenSettingsWindow(null);

#if WINDOWS
    private bool TryStartWindowsSetupMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var args = desktop.Args ?? Array.Empty<string>();
        bool isSetup = false;
        bool isUninstall = false;
        string? sourceDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--setup", StringComparison.OrdinalIgnoreCase))
            {
                isSetup = true;
            }
            else if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                isUninstall = true;
            }
            else if (string.Equals(args[i], "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                sourceDir = args[++i];
            }
        }

        if (!isSetup && !isUninstall)
            return false;

        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

        var platformHook = new WindowsSettingsPlatformService();
        var lang = platformHook.GetSystemPreferredLanguage() ?? string.Empty;
        var loc = new LocalizationService(lang);

        var setupStyle = WindowsPlatformChromeHelper.IsWindows10OrOlder()
            ? AppBackdropStyle.Solid
            : AppBackdropStyle.Translucent;
        ApplyBackdropStyle(setupStyle);
        ApplyThemeMode(AppThemeMode.System);

        var setupService = new WindowsSetupService(loc);
        var vm = new SetupViewModel(setupService, sourceDir, isUninstall, loc);
        var setupWindow = new SetupWindow(vm);
        WindowsPlatformChromeHelper.ConfigureWindowChrome(setupWindow, setupStyle);
        AttachPlatformBackdrop(setupWindow);

        var appIcon = LoadAppIcon();
        if (appIcon != null)
        {
            setupWindow.Icon = appIcon;
        }

        desktop.MainWindow = setupWindow;
        setupWindow.Show();
        return true;
    }
#endif

    public void OpenSettingsWindow(string? tab)
    {
        MemoryTrimHelper.CancelPendingTrim();
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel);
            WindowsPlatformChromeHelper.ConfigureWindowChrome(_settingsWindow, _settingsService.Settings.BackdropStyle);
            AttachPlatformBackdrop(_settingsWindow);
            var appIcon = LoadAppIcon();
            if (appIcon != null)
            {
                _settingsWindow.Icon = appIcon;
            }
            _settingsWindow.Closed += (s, e) =>
            {
                _settingsWindow = null;
                MemoryTrimHelper.ScheduleBackgroundTrim(_mainViewModel);
            };
        }

        if (!string.IsNullOrEmpty(tab))
        {
            _settingsWindow.SelectTab(tab);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    internal static WindowIcon? LoadAppIcon()
    {
        var asmName = typeof(App).Assembly.GetName().Name ?? "SlimClip";
        try
        {
            var uri = new Uri($"avares://{asmName}/Assets/app.ico");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
        }
        catch { }

        try
        {
            var uri = new Uri("avares://SlimClip.Desktop/Assets/app.ico");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
        }
        catch { }

        try
        {
            var uri = new Uri("avares://SlimClip.UI/Assets/app.png");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
        }
        catch { }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var iconPath = Path.Combine(appDir, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            return new WindowIcon(iconPath);
        }

        return null;
    }

    internal static WindowIcon? LoadTrayIcon()
    {
        var asmName = typeof(App).Assembly.GetName().Name ?? "SlimClip";
        try
        {
            var uri = new Uri($"avares://{asmName}/Assets/icon_tray.ico");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
        }
        catch { }

        try
        {
            var uri = new Uri("avares://SlimClip.Desktop/Assets/icon_tray.ico");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
        }
        catch { }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var iconPath = Path.Combine(appDir, "Assets", "icon_tray.ico");
        if (File.Exists(iconPath))
        {
            return new WindowIcon(iconPath);
        }

        return LoadAppIcon();
    }

    private void SetupTrayIcon()
    {
        var windowIcon = LoadTrayIcon();

        var menu = new NativeMenu();

        _showMenuItem = new NativeMenuItem(_localizationService["Tray_ShowHistory"]);
        UpdateTrayMenuHotkey(_settingsService.Settings.Hotkey);
        _showMenuItem.Click += (s, e) => ShowClipboardPanelNearAnchor();
        menu.Add(_showMenuItem);

        _settingsMenuItem = new NativeMenuItem(_localizationService["Tray_Settings"]);
        _settingsMenuItem.Click += (s, e) => OpenSettingsWindow();
        menu.Add(_settingsMenuItem);

        _checkUpdateMenuItem = new NativeMenuItem(_localizationService["Tray_CheckUpdate"]);
        _checkUpdateMenuItem.Click += (s, e) =>
        {
            OpenSettingsWindow("About");
            if (_settingsViewModel.CheckUpdatesCommand.CanExecute(null))
            {
                _settingsViewModel.CheckUpdatesCommand.Execute(null);
            }
        };
        menu.Add(_checkUpdateMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        _exitMenuItem = new NativeMenuItem(_localizationService["Tray_Exit"]);
        _exitMenuItem.Click += (s, e) =>
        {
            _clipboardMonitor.Stop();
            _hotkeyService.Unregister();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };
        menu.Add(_exitMenuItem);

        _trayIcon = new TrayIcon
        {
            Icon = windowIcon,
            ToolTipText = _localizationService["Tray_ToolTip"],
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (s, e) => ShowClipboardPanelNearAnchor();

        var trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, trayIcons);
    }

    private void UpdateTrayMenuLanguage()
    {
        if (_showMenuItem != null)
        {
            _showMenuItem.Header = _localizationService["Tray_ShowHistory"];
        }
        if (_settingsMenuItem != null)
        {
            _settingsMenuItem.Header = _localizationService["Tray_Settings"];
        }
        if (_checkUpdateMenuItem != null)
        {
            _checkUpdateMenuItem.Header = _localizationService["Tray_CheckUpdate"];
        }
        if (_exitMenuItem != null)
        {
            _exitMenuItem.Header = _localizationService["Tray_Exit"];
        }
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = _localizationService["Tray_ToolTip"];
        }
    }

    private void UpdateTrayMenuHotkey(HotkeyConfig config)
    {
        if (_showMenuItem == null) return;
        _showMenuItem.Gesture = KeyGestureHelper.TryParse(config.DisplayString);
    }

    private void ApplyThemeMode(AppThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

#if WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var isDark = mode switch
            {
                AppThemeMode.Light => false,
                AppThemeMode.Dark => true,
                _ => ActualThemeVariant == ThemeVariant.Dark
            };

            WindowsAcrylicHelper.ApplyNativeMenuTheme(isDark);

            if (_clipboardPanel?.TryGetPlatformHandle()?.Handle is { } hPanel && hPanel != IntPtr.Zero)
            {
                WindowsAcrylicHelper.UpdateThemeMode(hPanel, isDark);
            }
            if (_settingsWindow?.TryGetPlatformHandle()?.Handle is { } hSettings && hSettings != IntPtr.Zero)
            {
                WindowsAcrylicHelper.UpdateThemeMode(hSettings, isDark);
            }
        }
#endif
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var isDark = mode switch
            {
                AppThemeMode.Light => false,
                AppThemeMode.Dark => true,
                _ => ActualThemeVariant == ThemeVariant.Dark || MacLiquidGlassHelper.IsSystemDark()
            };

            MacLiquidGlassHelper.UpdateAppAppearance(isDark);

            if (_clipboardPanel?.TryGetPlatformHandle() is { } hPanel)
            {
                var winPtr = GetNSWindow(hPanel);
                if (winPtr != IntPtr.Zero)
                    MacLiquidGlassHelper.UpdateThemeMode(winPtr, isDark);
            }
            if (_settingsWindow?.TryGetPlatformHandle() is { } hSettings)
            {
                var winPtr = GetNSWindow(hSettings);
                if (winPtr != IntPtr.Zero)
                    MacLiquidGlassHelper.UpdateThemeMode(winPtr, isDark);
            }
        }
    }

    private static IntPtr GetNSWindow(IPlatformHandle? platformHandle)
    {
        if (platformHandle == null || platformHandle.Handle == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            var nsWindowProp = platformHandle.GetType().GetProperty("NSWindow");
            if (nsWindowProp?.GetValue(platformHandle) is IntPtr winPtr && winPtr != IntPtr.Zero)
            {
                return winPtr;
            }
        }
        catch { }

        return MacLiquidGlassHelper.ResolveNSWindow(platformHandle.Handle);
    }

    private void ApplyBackdropStyle(AppBackdropStyle style)
    {
        bool isSolid = style == AppBackdropStyle.Solid;
        bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isWin10 = WindowsPlatformChromeHelper.IsWindows10OrOlder();

        try
        {
            if (isSolid)
            {
                if (_solidBackdropDict == null)
                {
                    var uri = new Uri("avares://SlimClip.UI/Themes/Fluent2Win10Override.axaml");
                    _solidBackdropDict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
                }
                if (!Resources.MergedDictionaries.Contains(_solidBackdropDict))
                {
                    Resources.MergedDictionaries.Add(_solidBackdropDict);
                }
            }
            else if (_solidBackdropDict != null)
            {
                Resources.MergedDictionaries.Remove(_solidBackdropDict);
            }
        }
        catch { }

        var metrics = WindowsPlatformChromeHelper.CalculateChromeMetrics(
            isWin, isWin10, style, 420, 560, 340, 400, 800, 1200);

        Resources["WindowCornerRadius"] = metrics.WindowCornerRadius;
        Resources["WindowShadowMargin"] = metrics.ShadowMargin;

        if (_clipboardPanel != null)
        {
            WindowsPlatformChromeHelper.ConfigureWindowChrome(_clipboardPanel, style);
            ApplyWindowPlatformBackdrop(_clipboardPanel);
        }

        if (_settingsWindow != null)
        {
            WindowsPlatformChromeHelper.ConfigureWindowChrome(_settingsWindow, style);
            ApplyWindowPlatformBackdrop(_settingsWindow);
        }
    }

    private void ApplyWindowPlatformBackdrop(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle;
        if (handle == null || handle.Value == IntPtr.Zero)
            return;

        bool isSolid = (_settingsService?.Settings.BackdropStyle ??
                        (WindowsPlatformChromeHelper.IsWindows10OrOlder() ? AppBackdropStyle.Solid : AppBackdropStyle.Translucent)) == AppBackdropStyle.Solid;

#if WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var isDark = ActualThemeVariant == ThemeVariant.Dark ||
                         (ActualThemeVariant == ThemeVariant.Default && (_settingsService?.Settings.ThemeMode ?? AppThemeMode.System) != AppThemeMode.Light);
            WindowsAcrylicHelper.ApplyDesktopAcrylic(handle.Value, isDark, isSolid);
        }
#endif
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var isDark = (_settingsService?.Settings.ThemeMode ?? AppThemeMode.System) switch
            {
                AppThemeMode.Light => false,
                AppThemeMode.Dark => true,
                _ => ActualThemeVariant == ThemeVariant.Dark || MacLiquidGlassHelper.IsSystemDark()
            };

            var targetHandle = GetNSWindow(window.TryGetPlatformHandle());
            if (targetHandle == IntPtr.Zero)
                targetHandle = handle.Value;

            MacLiquidGlassHelper.ApplyLiquidGlass(targetHandle, isDark, isSolid);
        }
    }

    private void AttachPlatformBackdrop(Window window)
    {
        window.Opened += (s, e) => ApplyWindowPlatformBackdrop(window);
        window.Activated += (s, e) => ApplyWindowPlatformBackdrop(window);
        if (window.IsLoaded)
        {
            ApplyWindowPlatformBackdrop(window);
        }
    }

    private static void ConfigureMacAccessoryApp()
    {
        MacLiquidGlassHelper.ConfigureAccessoryApp();
    }
}
