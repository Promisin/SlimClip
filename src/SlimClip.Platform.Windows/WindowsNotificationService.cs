using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using SlimClip.Core.Services;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SlimClip.Platform.Windows;

/// <summary>
/// 基于 Windows 10/11 WinRT ToastNotificationManager 的原生系统通知实现。
/// 兼容便携版与安装版（通过当前用户 HKCU 注册 AppUserModelId，无需管理员权限与额外托盘图标）。
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    public const string AppUserModelId = "com.promisin.slimclip";

    private readonly object _syncLock = new();
    private ToastNotification? _activeToast;
    private bool _aumidRegistered;
    private bool _disposed;

    public void ShowNotification(string title, string body, Action? onClicked = null)
    {
        if (_disposed)
            return;

        try
        {
            EnsureAppUserModelIdRegistered();

            var iconPath = ResolveToastLogoPath();
            var xml = BuildToastXml(title, body, iconPath);
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var toast = new ToastNotification(doc);
            Action? clickHandler = onClicked;

            toast.Activated += (_, _) =>
            {
                ClearActiveToast(toast);
                clickHandler?.Invoke();
            };

            toast.Dismissed += (_, _) => ClearActiveToast(toast);
            toast.Failed += (_, _) => ClearActiveToast(toast);

            lock (_syncLock)
            {
                if (_disposed)
                    return;

                _activeToast = toast;
            }

            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
        }
        catch
        {
            // 系统通知受专注模式、组策略或系统组件异常影响时静默降级，不中断主应用运行
        }
    }

    internal static string BuildToastXml(string title, string body, string? iconPath)
    {
        var escapedTitle = SecurityElement.Escape(title) ?? string.Empty;
        var escapedBody = SecurityElement.Escape(body) ?? string.Empty;

        var imageElement = string.Empty;
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var escapedIcon = SecurityElement.Escape(iconPath) ?? string.Empty;
            imageElement = $"<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"{escapedIcon}\"/>";
        }

        return "<toast activationType=\"foreground\">"
             + "<visual><binding template=\"ToastGeneric\">"
             + $"<text>{escapedTitle}</text>"
             + $"<text>{escapedBody}</text>"
             + imageElement
             + "</binding></visual>"
             + "</toast>";
    }

    private void ClearActiveToast(ToastNotification toast)
    {
        lock (_syncLock)
        {
            if (ReferenceEquals(_activeToast, toast))
            {
                _activeToast = null;
            }
        }
    }

    private void EnsureAppUserModelIdRegistered()
    {
        if (_aumidRegistered)
            return;

        try
        {
            _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}", writable: true);
            if (key != null)
            {
                key.SetValue("DisplayName", "SlimClip", RegistryValueKind.String);
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);

                var iconPath = ResolveToastLogoPath();
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    key.SetValue("IconUri", iconPath, RegistryValueKind.String);
                }
            }

            _aumidRegistered = true;
        }
        catch
        {
        }
    }

    private static string? ResolveToastLogoPath()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var pngPath = Path.Combine(baseDir, "Assets", "app.png");
            if (File.Exists(pngPath))
                return pngPath;

            var icoPath = Path.Combine(baseDir, "Assets", "app.ico");
            if (File.Exists(icoPath))
                return icoPath;
        }
        catch
        {
        }

        return null;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _disposed = true;
            _activeToast = null;
        }
    }
}
