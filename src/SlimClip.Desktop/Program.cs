using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

using SlimClip.UI.Fonts;

namespace SlimClip.Desktop;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 尽早设置 macOS ActivationPolicy 为 Accessory (1)
        // 确保无论是裸二进制 (dotnet run) 还是打包 App，均不在 Dock 栏显示与占位
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SlimClip.Platform.MacOS.MacLiquidGlassHelper.ConfigureAccessoryApp();
        }

        var mutexName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"Local\SlimClip_SingleInstance_Mutex"
            : "SlimClip_SingleInstance_Mutex";
        bool acquired = false;
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            acquired = createdNew;
            if (!acquired)
            {
                // Wait briefly (up to 2s) in case previous instance is exiting during restart / elevation
                acquired = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(2), false);
            }
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            // Another instance is already running
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Ignored if mutex was abandoned or already released
            }
            _singleInstanceMutex?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithHarmonyOSFont()
            .WithInterFont()
            .LogToTrace();
}
