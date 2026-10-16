using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace SlimClip.UI.Converters;

public static class KeyMapper
{
    public static int AvaloniaKeyToPlatformCode(Key key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return AvaloniaKeyToMacVirtualKey(key);
        }

        return AvaloniaKeyToWindowsVk(key);
    }

    public static int AvaloniaKeyToWindowsVk(Key key)
    {
        // Letters A-Z
        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + (key - Key.A); // 0x41 = 'A' = 65
        }

        // Digits 0-9
        if (key >= Key.D0 && key <= Key.D9)
        {
            return 0x30 + (key - Key.D0); // 0x30 = '0' = 48
        }

        // NumPad 0-9
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }

        // Function keys F1-F24
        if (key >= Key.F1 && key <= Key.F24)
        {
            return 0x70 + (key - Key.F1); // 0x70 = VK_F1 = 112
        }

        // Special keys
        return key switch
        {
            Key.Space => 0x20,
            Key.Tab => 0x09,
            Key.Enter or Key.Return => 0x0D,
            Key.Escape => 0x1B,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.OemTilde => 0xC0,
            Key.OemMinus => 0xBD,
            Key.OemPlus => 0xBB,
            Key.OemOpenBrackets => 0xDB,
            Key.OemCloseBrackets => 0xDD,
            Key.OemPipe => 0xDC,
            Key.OemSemicolon => 0xBA,
            Key.OemQuotes => 0xDE,
            Key.OemComma => 0xBC,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            _ => (int)key
        };
    }

    public static int AvaloniaKeyToMacVirtualKey(Key key)
    {
        return key switch
        {
            Key.A => 0x00,
            Key.S => 0x01,
            Key.D => 0x02,
            Key.F => 0x03,
            Key.H => 0x04,
            Key.G => 0x05,
            Key.Z => 0x06,
            Key.X => 0x07,
            Key.C => 0x08,
            Key.V => 0x09,
            Key.B => 0x0B,
            Key.Q => 0x0C,
            Key.W => 0x0D,
            Key.E => 0x0E,
            Key.R => 0x0F,
            Key.Y => 0x10,
            Key.T => 0x11,
            Key.D1 => 0x12,
            Key.D2 => 0x13,
            Key.D3 => 0x14,
            Key.D4 => 0x15,
            Key.D6 => 0x16,
            Key.D5 => 0x17,
            Key.OemPlus => 0x18,
            Key.D9 => 0x19,
            Key.D7 => 0x1A,
            Key.OemMinus => 0x1B,
            Key.D8 => 0x1C,
            Key.D0 => 0x1D,
            Key.OemCloseBrackets => 0x1E,
            Key.O => 0x1F,
            Key.U => 0x20,
            Key.OemOpenBrackets => 0x21,
            Key.I => 0x22,
            Key.P => 0x23,
            Key.L => 0x25,
            Key.J => 0x26,
            Key.OemQuotes => 0x27,
            Key.K => 0x28,
            Key.OemSemicolon => 0x29,
            Key.OemPipe => 0x2A,
            Key.OemComma => 0x2B,
            Key.OemQuestion => 0x2C,
            Key.N => 0x2D,
            Key.M => 0x2E,
            Key.OemPeriod => 0x2F,
            Key.OemTilde => 0x32,
            Key.Space => 0x31,
            Key.Enter or Key.Return => 0x24,
            Key.Tab => 0x30,
            Key.Escape => 0x35,
            _ => (int)key
        };
    }
}
