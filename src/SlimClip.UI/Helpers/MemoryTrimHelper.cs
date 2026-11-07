using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SlimClip.UI.Converters;
using SlimClip.UI.ViewModels;

namespace SlimClip.UI.Helpers;

/// <summary>
/// 窗口隐藏或关闭退回后台后的轻量级空闲内存整顿工具。
/// 保留预热窗口句柄、DWM 亚克力表面与首屏热数据，仅回收多余翻页容器、超额缩略图与空闲物理页。
/// </summary>
public static partial class MemoryTrimHelper
{
    private static CancellationTokenSource? _trimCts;
    private static readonly object _lock = new();

    public static void ScheduleBackgroundTrim(MainViewModel? mainViewModel = null, int delayMs = 1500)
    {
        CancellationToken token;
        lock (_lock)
        {
            _trimCts?.Cancel();
            _trimCts?.Dispose();
            _trimCts = new CancellationTokenSource();
            token = _trimCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
                if (token.IsCancellationRequested)
                    return;

                if (mainViewModel != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            mainViewModel.TrimToFirstPageIfNeeded();
                        }
                    }, DispatcherPriority.Background);
                }

                if (token.IsCancellationRequested)
                    return;

                ImagePathToBitmapConverter.TrimCache(12);

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    TryEmptyWindowsWorkingSet();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }, token);
    }

    public static void CancelPendingTrim()
    {
        lock (_lock)
        {
            _trimCts?.Cancel();
        }
    }

    private static void TryEmptyWindowsWorkingSet()
    {
        try
        {
            var hProcess = GetCurrentProcess();
            if (hProcess != IntPtr.Zero)
            {
                K32EmptyWorkingSet(hProcess);
            }
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool K32EmptyWorkingSet(IntPtr hProcess);
}
