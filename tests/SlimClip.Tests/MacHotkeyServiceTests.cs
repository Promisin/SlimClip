using System;
using System.IO;
using System.Text.Json;
using SlimClip.Core.Models;
using SlimClip.Core.Services;
using SlimClip.Platform.MacOS;
using Xunit;

namespace SlimClip.Tests;

public class MacHotkeyServiceTests
{
    [Fact]
    public void MacHotkeyService_Register_ReturnsTrueOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var service = new MacHotkeyService();
        var config = new HotkeyConfig
        {
            KeyCode = 9, // 'V'
            Modifiers = KeyModifiers.WindowsOrCommand | KeyModifiers.Shift,
            IsEnabled = true
        };

        var registered = service.Register(config);
        Assert.True(registered, "MacHotkeyService.Register must succeed without requiring accessibility permission.");

        service.Unregister();
    }

    [Fact]
    public void MacHotkeyService_Register_DisabledConfig_Succeeds()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var service = new MacHotkeyService();
        var config = new HotkeyConfig
        {
            KeyCode = 9,
            Modifiers = KeyModifiers.WindowsOrCommand | KeyModifiers.Shift,
            IsEnabled = false
        };

        var registered = service.Register(config);
        Assert.True(registered);
    }

    [Fact]
    public void SettingsService_AutoHeals_SingleModifierHotkey_OnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), "SlimClip_Settings_Heal_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var settingsFile = Path.Combine(tempDir, "settings.json");

            // Write a broken config that only has Shift modifier (Modifiers = 4, "⇧V")
            var brokenJson = """
            {
              "Hotkey": {
                "Modifiers": 4,
                "KeyCode": 9,
                "DisplayString": "⇧V",
                "IsEnabled": true
              }
            }
            """;
            File.WriteAllText(settingsFile, brokenJson);

            var service = new SettingsService(customDataDir: tempDir);

            // Should have auto-healed to default ⇧⌘V (KeyModifiers.WindowsOrCommand | KeyModifiers.Shift = 12)
            Assert.True(service.Settings.Hotkey.Modifiers.HasFlag(KeyModifiers.WindowsOrCommand));
            Assert.True(service.Settings.Hotkey.Modifiers.HasFlag(KeyModifiers.Shift));
            Assert.Equal("⇧⌘V", service.Settings.Hotkey.DisplayString);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
