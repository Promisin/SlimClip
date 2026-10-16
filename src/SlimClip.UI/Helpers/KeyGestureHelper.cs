using Avalonia.Input;

namespace SlimClip.UI.Helpers;

public static class KeyGestureHelper
{
    /// <summary>
    /// 将跨平台快捷键展示字符串（包括 macOS 符号 ⇧⌘V 或 Windows 格式 Alt + Shift + V）
    /// 安全解析为 Avalonia KeyGesture。
    /// </summary>
    public static KeyGesture? TryParse(string? displayString)
    {
        if (string.IsNullOrWhiteSpace(displayString) || displayString == "无")
            return null;

        try
        {
            var str = displayString
                .Replace(" ", "")
                .Replace("⌃", "Ctrl+")
                .Replace("⌥", "Alt+")
                .Replace("⇧", "Shift+")
                .Replace("⌘", "Cmd+");

            return KeyGesture.Parse(str);
        }
        catch
        {
            return null;
        }
    }
}
