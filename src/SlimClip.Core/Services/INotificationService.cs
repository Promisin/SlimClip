using System;

namespace SlimClip.Core.Services;

/// <summary>
/// 跨平台系统原生通知服务契约。
/// </summary>
public interface INotificationService : IDisposable
{
    /// <summary>
    /// 弹出系统通知；当用户点击通知且当前平台支持激活回调时触发 <paramref name="onClicked"/>。
    /// 回调可能来自后台或系统线程，调用方需负责向 UI 主线程派发。
    /// </summary>
    void ShowNotification(string title, string body, Action? onClicked = null);
}
