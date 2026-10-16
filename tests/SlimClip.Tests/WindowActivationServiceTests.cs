using System;
using SlimClip.Core.Services;
using Xunit;

namespace SlimClip.Tests;

public class WindowActivationServiceTests
{
    private class FakeActivationService : IWindowActivationService
    {
        public IntPtr LastActivatedHandle { get; private set; } = IntPtr.Zero;
        public int ActivationCount { get; private set; }

        public IntPtr LastRestoredHandle { get; private set; } = IntPtr.Zero;
        public int RestoreCount { get; private set; }

        public void ActivateWindow(IntPtr windowHandle)
        {
            LastActivatedHandle = windowHandle;
            ActivationCount++;
        }

        public void RestoreForegroundWindow(IntPtr windowHandle)
        {
            LastRestoredHandle = windowHandle;
            RestoreCount++;
        }
    }

    private class DefaultActivationService : IWindowActivationService
    {
        public void ActivateWindow(IntPtr windowHandle) { }
    }

    [Fact]
    public void WindowActivationService_FakeContract_WorksCorrectly()
    {
        var service = new FakeActivationService();
        var testHandle = new IntPtr(12345);

        service.ActivateWindow(testHandle);

        Assert.Equal(1, service.ActivationCount);
        Assert.Equal(testHandle, service.LastActivatedHandle);

        service.RestoreForegroundWindow(testHandle);
        Assert.Equal(1, service.RestoreCount);
        Assert.Equal(testHandle, service.LastRestoredHandle);
    }

    [Fact]
    public void WindowActivationService_ZeroHandle_DoesNotThrow()
    {
        var service = new FakeActivationService();
        service.ActivateWindow(IntPtr.Zero);

        Assert.Equal(1, service.ActivationCount);
        Assert.Equal(IntPtr.Zero, service.LastActivatedHandle);

        service.RestoreForegroundWindow(IntPtr.Zero);
        Assert.Equal(1, service.RestoreCount);
        Assert.Equal(IntPtr.Zero, service.LastRestoredHandle);
    }

    [Fact]
    public void WindowActivationService_DefaultInterfaceImplementation_DoesNotThrow()
    {
        IWindowActivationService service = new DefaultActivationService();
        service.RestoreForegroundWindow(new IntPtr(999));
        Assert.Equal(IntPtr.Zero, service.GetForegroundWindowHandle());
    }
}
