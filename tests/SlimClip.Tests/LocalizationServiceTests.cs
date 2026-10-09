using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using SlimClip.UI.Views;
using Xunit;

namespace SlimClip.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void Settings_Language_WhenPersisted_MustSurviveAcrossServiceRestarts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var service1 = new SettingsService(null, tempDir);
            service1.Settings.Language = "zh-CN";
            service1.Save();

            // Verify physical file content
            var jsonPath = Path.Combine(tempDir, "settings.json");
            Assert.True(File.Exists(jsonPath));
            var rawJson = File.ReadAllText(jsonPath);
            Assert.Contains("\"Language\": \"zh-CN\"", rawJson);

            // Simulate application restart
            var service2 = new SettingsService(null, tempDir);
            Assert.Equal("zh-CN", service2.Settings.Language);

            var loc = new LocalizationService(service2.Settings.Language);
            Assert.Equal("zh-CN", loc.CurrentLanguage);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData("zh-Hans-US", "zh-CN")]
    [InlineData("zh-Hans-CN", "zh-CN")]
    [InlineData("zh-Hant-HK", "zh-CN")]
    [InlineData("zh-Hant-TW", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hant", "zh-CN")]
    [InlineData("zh", "zh-CN")]
    public void DetectDefaultLanguage_RecognizesAllChineseVariants(string cultureInput, string expected)
    {
        var detected = LocalizationService.DetectDefaultLanguage(cultureInput);
        Assert.Equal(expected, detected);
    }

    [Fact]
    public void SettingsService_WhenLanguageEmpty_PrefersPlatformHookLanguage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Test_" + Guid.NewGuid().ToString("N"));
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            // Simulate macOS environment with LANG=C.UTF-8 (CultureInfo.InvariantCulture)
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

            var mockHook = new MockLanguagePlatformHook("zh-Hans-US");
            var service = new SettingsService(mockHook, tempDir);
            Assert.Equal("zh-CN", service.Settings.Language);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private class MockLanguagePlatformHook : IPlatformSettingsHook
    {
        private readonly string? _lang;
        public MockLanguagePlatformHook(string? lang) => _lang = lang;
        public bool SetAutoStart(bool enabled) => true;
        public Task<bool> RequestElevationRestartAsync() => Task.FromResult(false);
        public bool IsAutoStartEnabled() => false;
        public string? GetSystemPreferredLanguage() => _lang;
    }



    [Fact]
    public void SupportedLanguages_Contains_ChineseAndEnglish()
    {
        var service = new LocalizationService();
        Assert.NotNull(service.SupportedLanguages);
        Assert.Contains(service.SupportedLanguages, l => l.Code == "zh-CN");
        Assert.Contains(service.SupportedLanguages, l => l.Code == "en-US");
    }

    [Theory]
    [InlineData("zh-CN", "Category_All", "全部")]
    [InlineData("en-US", "Category_All", "All")]
    [InlineData("zh-CN", "Category_Favorite", "收藏")]
    [InlineData("en-US", "Category_Favorite", "Favorite")]
    public void GetString_ReturnsExpectedTranslation(string culture, string key, string expected)
    {
        var service = new LocalizationService();
        service.SetLanguage(culture);
        Assert.Equal(expected, service[key]);
    }

    [Fact]
    public void GetString_FallbackToEnglish_WhenKeyNotFoundInChinese()
    {
        var service = new LocalizationService();
        service.SetLanguage("zh-CN");
        // Unknown key should fallback to key itself
        Assert.Equal("NonExistent_Key", service["NonExistent_Key"]);
    }

    [Fact]
    public void SetLanguage_TriggersLanguageChangedEvent()
    {
        var service = new LocalizationService("zh-CN");
        string? changedLanguage = null;
        service.LanguageChanged += (s, lang) => changedLanguage = lang;

        service.SetLanguage("en-US");
        Assert.Equal("en-US", changedLanguage);
        Assert.Equal("en-US", service.CurrentLanguage);
    }


    [Theory]
    [InlineData("zh-CN", 0, "刚刚")]
    [InlineData("en-US", 0, "Just now")]
    [InlineData("zh-CN", 5, "5分钟前")]
    [InlineData("en-US", 5, "5m ago")]
    [InlineData("zh-CN", 120, "2小时前")]
    [InlineData("en-US", 120, "2h ago")]
    public void FormatRelativeTime_ReturnsExpectedLocalizedText(string culture, int minutesAgo, string expected)
    {
        var service = new LocalizationService();
        service.SetLanguage(culture);
        var targetTime = DateTime.Now.AddMinutes(-minutesAgo);
        Assert.Equal(expected, service.FormatRelativeTime(targetTime));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("zh-HK", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("", "en-US")]
    public void DetectDefaultLanguage_ProbesCorrectly(string cultureInput, string expected)
    {
        var detected = LocalizationService.DetectDefaultLanguage(cultureInput);
        Assert.Equal(expected, detected);
    }

    [Fact]
    public void LocaleFiles_HaveIdenticalKeySets()
    {
        var asm = typeof(LocalizationService).Assembly;
        using var zhStream = asm.GetManifestResourceStream("SlimClip.Core.Assets.Locales.zh-CN.json");
        using var enStream = asm.GetManifestResourceStream("SlimClip.Core.Assets.Locales.en-US.json");

        Assert.NotNull(zhStream);
        Assert.NotNull(enStream);

        var zhDict = JsonSerializer.Deserialize<Dictionary<string, string>>(zhStream);
        var enDict = JsonSerializer.Deserialize<Dictionary<string, string>>(enStream);

        Assert.NotNull(zhDict);
        Assert.NotNull(enDict);

        var zhOnly = zhDict.Keys.Except(enDict.Keys).ToList();
        var enOnly = enDict.Keys.Except(zhDict.Keys).ToList();

        Assert.Empty(zhOnly);
        Assert.Empty(enOnly);
    }

    private class DummyHistory : IClipboardHistory
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<ClipboardItem> RecordAsync(ClipboardData data) => throw new NotImplementedException();
        public Task<List<ClipboardItem>> GetAllItemsAsync() => Task.FromResult(new List<ClipboardItem>());
        public Task<bool> DeleteItemAsync(string id) => Task.FromResult(true);
        public Task<bool> ToggleFavoriteAsync(string id) => Task.FromResult(true);
        public Task ClearAllAsync() => Task.CompletedTask;
        public Task TrimExcessAsync(int maxItems) => Task.CompletedTask;
        public Task CleanPeriodicAsync(int days) => Task.CompletedTask;
        public Task UpdateTimestampAsync(string id, DateTime timestamp) => Task.CompletedTask;
        public Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt) => Task.FromResult(true);
        public void Dispose() { }
    }

    [Fact]
    public void SettingsViewModel_SwitchingLanguage_UpdatesSettingsAndProperties()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "zh-CN";
        var locService = new LocalizationService("zh-CN");
        var fakeHistory = new DummyHistory();

        var vm = new SettingsViewModel(settingsService, fakeHistory, null, locService);

        Assert.Equal("zh-CN", vm.SelectedLanguage.Code);
        Assert.Equal("复制信息", vm.CopyVersionButtonText);
        Assert.Equal("条", vm.MaxItemsUnitDisplay);

        var enLang = vm.SupportedLanguages.First(l => l.Code == "en-US");
        string? firedLang = null;
        vm.LanguageChanged += (s, lang) => firedLang = lang;

        vm.SelectedLanguage = enLang;

        Assert.Equal("en-US", firedLang);
        Assert.Equal("en-US", settingsService.Settings.Language);
        Assert.Equal("en-US", locService.CurrentLanguage);
        Assert.Equal("Copy Info", vm.CopyVersionButtonText);
        Assert.Equal("items", vm.MaxItemsUnitDisplay);
    }
}
