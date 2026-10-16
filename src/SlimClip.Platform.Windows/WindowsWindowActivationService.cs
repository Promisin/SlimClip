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

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
        NativeMethods.BringWindowToTop(hWnd);

        if (NativeMethods.SetForegroundWindow(hWnd))
        {
            return true;
        }

        // 若直接激活失败，尝试辅助激活，但坚决不对 Explorer 进程 AttachThreadInput（防止意外弹出系统开始菜单）
        IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
        uint foregroundThreadId = 0;
        uint processId = 0;
        if (foregroundHwnd != IntPtr.Zero)
        {
            foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out processId);
        }
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        bool isExplorer = false;
        if (foregroundHwnd != IntPtr.Zero)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                if (string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                {
                    isExplorer = true;
                }
            }
            catch { }
        }

        if (!isExplorer && foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            bool attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            try
            {
                NativeMethods.BringWindowToTop(hWnd);
                return NativeMethods.SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }

        return false;
    }
}
