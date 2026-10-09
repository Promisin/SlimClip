using System;
using System.Collections.Generic;
using System.ComponentModel;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    IReadOnlyList<LanguageInfo> SupportedLanguages { get; }
    string this[string key] { get; }
    string GetString(string key, params object[] args);
    string FormatRelativeTime(DateTime dateTime);
    void SetLanguage(string cultureCode);
    event EventHandler<string>? LanguageChanged;
}
