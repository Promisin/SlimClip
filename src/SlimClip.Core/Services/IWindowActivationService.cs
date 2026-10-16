using System;

namespace SlimClip.Core.Services;

/// <summary>
/// 跨平台窗口激活与前台焦点抢占服务契约
/// </summary>
public interface IWindowActivationService
{
    /// <summary>
    /// 将指定原生窗口句柄强制激活并提升为系统前台活动窗口
    /// </summary>
    /// <param name="windowHandle">原生窗口句柄（Windows: HWND, macOS: NSWindow 指针）</param>
    void ActivateWindow(IntPtr windowHandle);

    /// <summary>
    /// 获取当前系统前台活动窗口句柄
    /// </summary>
    IntPtr GetForegroundWindowHandle() => IntPtr.Zero;

    /// <summary>
    /// 恢复并激活指定原生窗口句柄或目标应用程序（切回唤醒前的输入宿主）
    /// </summary>
    /// <param name="windowHandle">原生窗口句柄（Windows: HWND, macOS: 进程 PID）</param>
    void RestoreForegroundWindow(IntPtr windowHandle) { }
}
