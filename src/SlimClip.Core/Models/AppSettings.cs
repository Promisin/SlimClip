using System.Runtime.InteropServices;

namespace SlimClip.Core.Models;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public class AppSettings
{
    public HotkeyConfig Hotkey { get; set; } = new();
    public int MaxHistoryItems { get; set; } = 100;
    public bool StartWithSystem { get; set; }
    public bool RunInBackground { get; set; } = true;
    public bool AutoCleanEnabled { get; set; }
    public bool PeriodicCleanEnabled { get; set; }
    public int CleanPeriodDays { get; set; } = 7;
    public bool StartEmptyHistory { get; set; }
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public string Language { get; set; } = string.Empty;
    public int PasteDelayMs { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 80 : 60;
    public int SettingsWindowX { get; set; }
    public int SettingsWindowY { get; set; }
    public int SettingsWindowWidth { get; set; } = 560;
    public int SettingsWindowHeight { get; set; } = 540;
}
