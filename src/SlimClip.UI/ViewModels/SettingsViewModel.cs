using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.Helpers;

namespace SlimClip.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IClipboardHistory _history;
    private readonly IHotkeyService? _hotkeyService;
    private readonly ILocalizationService _localizationService;

    public ILocalizationService L => _localizationService;
    public IReadOnlyList<LanguageInfo> SupportedLanguages => _localizationService.SupportedLanguages;

    private LanguageInfo _selectedLanguage;
    public LanguageInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage != value && value != null)
            {
                _selectedLanguage = value;
                _settingsService.Settings.Language = value.Code;
                _settingsService.Save();
                _localizationService.SetLanguage(value.Code);
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecordHotkeyButtonText));
                LanguageChanged?.Invoke(this, value.Code);
            }
        }
    }

    public event EventHandler<string>? LanguageChanged;

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordHotkeyButtonText))]
    private bool _isRecordingHotkey;

    public string RecordHotkeyButtonText => IsRecordingHotkey
        ? _localizationService["Settings_Hotkey_Recording"]
        : _localizationService["Settings_Hotkey_Change"];

    public string MaxItemsUnitDisplay => _localizationService["Settings_Data_MaxItems_Unit"];
    public string CleanDaysUnitDisplay => _localizationService["Settings_Data_CleanDays_Unit"];
    public string PasteDelayUnitDisplay => _localizationService["Settings_Advanced_PasteDelay_Unit"];

    [ObservableProperty]
    private string _hotkeyDisplay;

    public SettingsViewModel(
        ISettingsService settingsService,
        IClipboardHistory history,
        IHotkeyService? hotkeyService = null,
        ILocalizationService? localizationService = null)
    {
        _settingsService = settingsService;
        _history = history;
        _hotkeyService = hotkeyService;
        _localizationService = localizationService ?? new LocalizationService(_settingsService.Settings.Language);

        _selectedLanguage = _localizationService.SupportedLanguages.FirstOrDefault(l => l.Code == _localizationService.CurrentLanguage)
                            ?? _localizationService.SupportedLanguages[0];

        _localizationService.LanguageChanged += (s, lang) =>
        {
            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(L));
            OnPropertyChanged(nameof(RecordHotkeyButtonText));
            OnPropertyChanged(nameof(MaxItemsUnitDisplay));
            OnPropertyChanged(nameof(CleanDaysUnitDisplay));
            OnPropertyChanged(nameof(PasteDelayUnitDisplay));
            OnPropertyChanged(nameof(CopyVersionButtonText));
        };

        _hotkeyDisplay = _settingsService.Settings.Hotkey.DisplayString;

        if (_hotkeyService != null)
        {
            _hotkeyService.HotkeyRecorded += OnPlatformHotkeyRecorded;
        }
    }

    private void OnPlatformHotkeyRecorded(object? sender, HotkeyConfig config)
    {
        ApplyRecordedHotkey(config.Modifiers, config.KeyCode, config.DisplayString);
    }

    public bool StartWithSystem
    {
        get => _settingsService.Settings.StartWithSystem;
        set
        {
            if (_settingsService.Settings.StartWithSystem != value)
            {
                _settingsService.SetAutoStart(value);
                OnPropertyChanged();
            }
        }
    }

    public bool StartEmptyHistory
    {
        get => _settingsService.Settings.StartEmptyHistory;
        set
        {
            if (_settingsService.Settings.StartEmptyHistory != value)
            {
                _settingsService.Settings.StartEmptyHistory = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public AppThemeMode ThemeMode
    {
        get => _settingsService.Settings.ThemeMode;
        set
        {
            if (_settingsService.Settings.ThemeMode != value)
            {
                _settingsService.Settings.ThemeMode = value;
                _settingsService.Save();
                OnPropertyChanged();
                ThemeModeChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<AppThemeMode>? ThemeModeChanged;

    public int MaxHistoryItems
    {
        get => _settingsService.Settings.MaxHistoryItems;
        set
        {
            if (_settingsService.Settings.MaxHistoryItems != value)
            {
                _settingsService.Settings.MaxHistoryItems = Math.Clamp(value, 10, 99999);
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoCleanEnabled
    {
        get => _settingsService.Settings.AutoCleanEnabled;
        set
        {
            if (_settingsService.Settings.AutoCleanEnabled != value)
            {
                _settingsService.Settings.AutoCleanEnabled = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool PeriodicCleanEnabled
    {
        get => _settingsService.Settings.PeriodicCleanEnabled;
        set
        {
            if (_settingsService.Settings.PeriodicCleanEnabled != value)
            {
                _settingsService.Settings.PeriodicCleanEnabled = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public int CleanPeriodDays
    {
        get => _settingsService.Settings.CleanPeriodDays;
        set
        {
            if (_settingsService.Settings.CleanPeriodDays != value)
            {
                _settingsService.Settings.CleanPeriodDays = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public int PasteDelayMs
    {
        get => _settingsService.Settings.PasteDelayMs;
        set
        {
            if (_settingsService.Settings.PasteDelayMs != value)
            {
                _settingsService.Settings.PasteDelayMs = Math.Clamp(value, 10, 240);
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    public void ResetPasteDelayDefault()
    {
        PasteDelayMs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 80 : 60;
    }

    [RelayCommand]
    public void StartRecordingHotkey()
    {
        IsRecordingHotkey = true;
        _hotkeyService?.StartRecording();
    }

    [RelayCommand]
    public void StopRecordingHotkey()
    {
        IsRecordingHotkey = false;
        _hotkeyService?.StopRecording();
    }

    [RelayCommand]
    public void ResetHotkeyDefault()
    {
        _hotkeyService?.StopRecording();
        var isMac = IsMacOS;
        _settingsService.Settings.Hotkey = new HotkeyConfig
        {
            Modifiers = isMac ? (KeyModifiers.WindowsOrCommand | KeyModifiers.Shift) : (KeyModifiers.Alt | KeyModifiers.Shift),
            KeyCode = isMac ? 9 : 86,
            DisplayString = isMac ? "⇧⌘V" : "Alt + Shift + V",
            IsEnabled = true
        };
        _settingsService.Save();
        HotkeyDisplay = _settingsService.Settings.Hotkey.DisplayString;
        IsRecordingHotkey = false;
        HotkeyChanged?.Invoke(this, _settingsService.Settings.Hotkey);
    }

    [RelayCommand]
    public void ClearHotkey()
    {
        _hotkeyService?.StopRecording();
        _settingsService.Settings.Hotkey.IsEnabled = false;
        _settingsService.Settings.Hotkey.DisplayString = "无";
        _settingsService.Save();
        HotkeyDisplay = "无";
        IsRecordingHotkey = false;
        HotkeyChanged?.Invoke(this, _settingsService.Settings.Hotkey);
    }

    public void ApplyRecordedHotkey(KeyModifiers modifiers, int keyCode, string display)
    {
        // Standalone keys without modifiers (except F1-F24) are not allowed as global hotkeys
        bool isFunctionKey = keyCode >= 0x70 && keyCode <= 0x87;
        if (modifiers == KeyModifiers.None && !isFunctionKey)
        {
            return;
        }

        _hotkeyService?.StopRecording();
        _settingsService.Settings.Hotkey.Modifiers = modifiers;
        _settingsService.Settings.Hotkey.KeyCode = keyCode;
        _settingsService.Settings.Hotkey.DisplayString = display;
        _settingsService.Settings.Hotkey.IsEnabled = true;
        _settingsService.Save();

        HotkeyDisplay = display;
        IsRecordingHotkey = false;
        HotkeyChanged?.Invoke(this, _settingsService.Settings.Hotkey);
    }

    public event EventHandler<HotkeyConfig>? HotkeyChanged;
    public event EventHandler? DataCleared;

    [RelayCommand]
    public async Task ClearAllDataAsync()
    {
        await _history.ClearAllAsync();
        DataCleared?.Invoke(this, EventArgs.Empty);
    }

    // --- About Panel Properties & Commands ---

    public string AppVersion => GetAppVersion();

    public string PlatformDisplay => GetPlatformDisplay();

    public string VersionInfoSummary => $"SlimClip {AppVersion} · {PlatformDisplay}";

    [ObservableProperty]
    private bool _isVersionCopied;

    public string CopyVersionButtonText => IsVersionCopied ? _localizationService["Settings_About_Copied"] : _localizationService["Settings_About_CopyInfo"];

    public event Action<string>? CopyToClipboardRequested;

    [RelayCommand]
    public async Task CopyVersionInfoAsync()
    {
        CopyToClipboardRequested?.Invoke(VersionInfoSummary);
        IsVersionCopied = true;
        OnPropertyChanged(nameof(CopyVersionButtonText));
        await Task.Delay(2000);
        IsVersionCopied = false;
        OnPropertyChanged(nameof(CopyVersionButtonText));
    }

    public const string ProjectUrl = "https://github.com/Promisin/SlimClip";
    public const string ReleasesUrl = "https://github.com/Promisin/SlimClip/releases";
    public const string IssuesUrl = "https://github.com/Promisin/SlimClip/issues";
    public const string LicenseUrl = "https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans";

    [RelayCommand]
    public void OpenProjectUrl() => PlatformLauncher.OpenUrl(ProjectUrl);

    [RelayCommand]
    public void CheckUpdates() => PlatformLauncher.OpenUrl(ReleasesUrl);

    [RelayCommand]
    public void OpenIssues() => PlatformLauncher.OpenUrl(IssuesUrl);

    [RelayCommand]
    public void OpenLicense() => PlatformLauncher.OpenUrl(LicenseUrl);

    [RelayCommand]
    public void OpenDataDirectory()
    {
        var dir = _settingsService.GetDataDirectory();
        PlatformLauncher.OpenFolder(dir);
    }

    private static string GetAppVersion()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var ver = asm.GetName().Version;
        return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.2";
    }

    private static string GetPlatformDisplay()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Apple Silicon" : "ARM64",
            Architecture.X64 => "x64",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"macOS ({arch})";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"Windows ({arch})";
        }
        return $"Desktop ({arch})";
    }
}
