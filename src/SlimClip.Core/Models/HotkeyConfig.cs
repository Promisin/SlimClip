using System;

namespace SlimClip.Core.Models;

[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    WindowsOrCommand = 8
}

public class HotkeyConfig
{
    public KeyModifiers Modifiers { get; set; }
    public int KeyCode { get; set; }
    public string DisplayString { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public HotkeyConfig()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Modifiers = KeyModifiers.WindowsOrCommand | KeyModifiers.Shift;
            KeyCode = 9; // kVK_ANSI_V
            DisplayString = "⇧⌘V";
        }
        else
        {
            Modifiers = KeyModifiers.Alt | KeyModifiers.Shift;
            KeyCode = 86; // VK_V
            DisplayString = "Alt + Shift + V";
        }
    }
}
