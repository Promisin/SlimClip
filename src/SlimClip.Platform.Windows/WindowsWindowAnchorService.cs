using System;
using System.Runtime.InteropServices;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsWindowAnchorService : IWindowAnchorService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeMethods.RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativeMethods.POINT lpPoint);

    public (int X, int Y) GetActivationAnchor()
    {
        // 1. Try UI Automation TextPattern Caret (modern apps: Chrome, Edge, VS Code, Word, WPF)
        var uiaCaret = GetUiaCaretPosition();
        if (uiaCaret.HasValue)
        {
            return uiaCaret.Value;
        }

        // 2. Try Caret position via GetGUIThreadInfo (legacy Win32 apps)
        try
        {
            var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(0, ref gui) && gui.hwndCaret != IntPtr.Zero)
            {
                var pt = new NativeMethods.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Bottom };
                if (ClientToScreen(gui.hwndCaret, ref pt))
                {
                    if (pt.X > 0 || pt.Y > 0)
                        return (pt.X, pt.Y);
                }
            }
        }
        catch { }

        // 3. Fallback to mouse cursor position
        if (NativeMethods.GetCursorPos(out var mousePt))
        {
            return (mousePt.X, mousePt.Y);
        }

        // 4. Fallback to primary screen center
        return (960, 540);
    }

    private static (int X, int Y)? GetUiaCaretPosition()
    {
        try
        {
            var automation = new Interop.UIAutomationClient.CUIAutomation();
            var focusedElement = automation.GetFocusedElement();
            if (focusedElement == null) return null;

            // UIA_TextPatternId = 10014
            if (focusedElement.GetCurrentPattern(10014) is Interop.UIAutomationClient.IUIAutomationTextPattern textPattern)
            {
                var selectionArray = textPattern.GetSelection();
                if (selectionArray != null && selectionArray.Length > 0)
                {
                    var textRange = selectionArray.GetElement(0);
                    var rects = (double[]?)textRange.GetBoundingRectangles();
                    if (rects != null && rects.Length >= 4)
                    {
                        int caretX = (int)rects[0];
                        int caretY = (int)(rects[1] + rects[3]);
                        if (caretX > 0 || caretY > 0)
                            return (caretX, caretY);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public (int X, int Y) CalculateWindowPosition(
        (int X, int Y)? anchor,
        int windowWidth,
        int windowHeight,
        int workAreaLeft,
        int workAreaTop,
        int workAreaRight,
        int workAreaBottom)
    {
        // If caller passed 0 workArea bounds, detect from monitor
        if (workAreaRight <= workAreaLeft || workAreaBottom <= workAreaTop)
        {
            var pt = anchor.HasValue
                ? new NativeMethods.POINT { X = anchor.Value.X, Y = anchor.Value.Y }
                : new NativeMethods.POINT { X = 0, Y = 0 };

            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfoW(hMon, ref mi))
            {
                workAreaLeft = mi.rcWork.Left;
                workAreaTop = mi.rcWork.Top;
                workAreaRight = mi.rcWork.Right;
                workAreaBottom = mi.rcWork.Bottom;
            }
            else
            {
                workAreaLeft = 0;
                workAreaTop = 0;
                workAreaRight = 1920;
                workAreaBottom = 1080;
            }
        }

        return WindowAnchorCalculator.Calculate(
            anchor,
            windowWidth,
            windowHeight,
            workAreaLeft,
            workAreaTop,
            workAreaRight,
            workAreaBottom);
    }
}
