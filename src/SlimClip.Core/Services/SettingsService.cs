using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IPlatformSettingsHook
{
    bool SetAutoStart(bool enabled);
    Task<bool> RequestElevationRestartAsync();
    bool IsAutoStartEnabled();
    bool IsElevated => false;
    string? GetSystemPreferredLanguage() => null;
}

public class SettingsService : ISettingsService
{
    private readonly string _dataDir;
    private readonly string _settingsFilePath;
    private readonly IPlatformSettingsHook? _platformHook;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public SettingsService(IPlatformSettingsHook? platformHook = null, string? customDataDir = null)
    {
        _platformHook = platformHook;
        _dataDir = customDataDir ?? ResolveDataDirectory();
        _settingsFilePath = Path.Combine(_dataDir, "settings.json");

        if (!Directory.Exists(_dataDir))
        {
            Directory.CreateDirectory(_dataDir);
        }

        Load();
    }

    public string GetDataDirectory() => _dataDir;

    public static string ResolveDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "SlimClip");
        }

        // On Windows and other OS, portable in application directory
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;

                    // Auto-heal broken or accidental single-modifier shortcuts on macOS
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        if (_settings.Hotkey == null ||
                            (!_settings.Hotkey.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand) &&
                             !_settings.Hotkey.Modifiers.HasFlag(KeyModifiers.Control) &&
                             !_settings.Hotkey.Modifiers.HasFlag(KeyModifiers.Alt)))
                        {
                            _settings.Hotkey = new HotkeyConfig();
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(_settings.Language))
            {
                var platformLang = _platformHook?.GetSystemPreferredLanguage();
                _settings.Language = LocalizationService.DetectDefaultLanguage(
                    !string.IsNullOrWhiteSpace(platformLang) ? platformLang : System.Globalization.CultureInfo.CurrentUICulture.Name);
                Save();
            }

            if (_platformHook != null)
            {
                _settings.StartWithSystem = _platformHook.IsAutoStartEnabled();
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }

    public Task<bool> RequestElevationRestartAsync()
    {
        if (_platformHook != null)
            return _platformHook.RequestElevationRestartAsync();
        return Task.FromResult(false);
    }

    public bool SetAutoStart(bool enabled)
    {
        _settings.StartWithSystem = enabled;
        Save();

        if (_platformHook != null)
            return _platformHook.SetAutoStart(enabled);
        return false;
    }

    public bool IsAutoStartEnabled()
    {
        if (_platformHook != null)
            return _platformHook.IsAutoStartEnabled();
        return _settings.StartWithSystem;
    }

    public bool IsElevated => _platformHook?.IsElevated ?? false;
}
