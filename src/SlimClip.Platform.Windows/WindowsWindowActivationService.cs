using System;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsWindowActivationService : IWindowActivationService
{
    public void ActivateWindow(IntPtr windowHandle)
    {
        ForceSetForegroundWindow(windowHandle);
    }

    public IntPtr GetForegroundWindowHandle()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public void RestoreForegroundWindow(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            ForceSetForegroundWindow(windowHandle);
        }
    }

    public static bool ForceSetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
        uint foregroundThreadId = foregroundHwnd != IntPtr.Zero
            ? NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _)
            : 0;
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        bool attached = false;
        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
        }

        try
        {
            // 允许任意进程激活前台
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

            // 产生无害空击键刷新 Windows 前台锁定判定
            NativeMethods.keybd_event(0, 0, 0, UIntPtr.Zero);

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            NativeMethods.BringWindowToTop(hWnd);
            bool result = NativeMethods.SetForegroundWindow(hWnd);
            return result;
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }
}
