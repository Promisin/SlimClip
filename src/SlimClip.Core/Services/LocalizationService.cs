using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public class LocalizationService : ILocalizationService
{
    private static readonly LanguageInfo[] _supportedLanguages =
    [
        new("zh-CN", "简体中文", "Chinese (Simplified)"),
        new("en-US", "English", "English (United States)")
    ];

    private readonly Dictionary<string, Dictionary<string, string>> _locales = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "zh-CN";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? LanguageChanged;

    public LocalizationService(string? initialLanguage = null)
    {
        LoadEmbeddedLocales();

        var lang = !string.IsNullOrEmpty(initialLanguage)
            ? initialLanguage
            : DetectDefaultLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name);

        SetLanguage(lang, notify: false);
    }

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<LanguageInfo> SupportedLanguages => _supportedLanguages;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 1. Try current language
            if (_locales.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }

            // 2. Fallback to English
            if (_currentLanguage != "en-US" &&
                _locales.TryGetValue("en-US", out var fallbackDict) &&
                fallbackDict.TryGetValue(key, out var fallbackVal))
            {
                return fallbackVal;
            }

            // 3. Fallback to key itself
            return key;
        }
    }

    public string GetString(string key, params object[] args)
    {
        var template = this[key];
        if (args != null && args.Length > 0)
        {
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }
        return template;
    }

    public string FormatRelativeTime(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;
        if (span.TotalMinutes < 1)
        {
            return this["Time_JustNow"];
        }
        if (span.TotalMinutes < 60)
        {
            return GetString("Time_MinutesAgo", (int)span.TotalMinutes);
        }
        if (span.TotalHours < 24)
        {
            return GetString("Time_HoursAgo", (int)span.TotalHours);
        }
        if (span.TotalDays < 2)
        {
            return this["Time_Yesterday"];
        }
        return dateTime.ToString("yyyy-MM-dd");
    }

    public void SetLanguage(string cultureCode) => SetLanguage(cultureCode, notify: true);

    private void SetLanguage(string cultureCode, bool notify)
    {
        var targetCode = NormalizeLanguageCode(cultureCode);
        if (_currentLanguage == targetCode && notify)
            return;

        _currentLanguage = targetCode;

        if (notify)
        {
            LanguageChanged?.Invoke(this, _currentLanguage);
            // Notify WPF/Avalonia bindings that indexer values have changed
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    public static string DetectDefaultLanguage(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return "en-US";

        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        return "en-US";
    }

    private static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "en-US";

        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        return "en-US";
    }

    private void LoadEmbeddedLocales()
    {
        var assembly = typeof(LocalizationService).Assembly;
        var prefix = "SlimClip.Core.Assets.Locales.";

        foreach (var lang in _supportedLanguages)
        {
            var resourceName = $"{prefix}{lang.Code}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    _locales[lang.Code] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }
}
