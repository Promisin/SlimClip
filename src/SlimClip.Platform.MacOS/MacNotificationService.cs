using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SlimClip.Core.Services;

namespace SlimClip.Platform.MacOS;

/// <summary>
/// macOS 系统通知实现：
/// 打包在 .app Bundle（含 CFBundleIdentifier）中时使用原生 UNUserNotificationCenter，支持权限申请与点击唤醒应用；
/// 开发期裸二进制运行（dotnet run 无 Bundle）时安全降级为 osascript display notification，避免系统抛出 Bundle 异常崩溃。
/// </summary>
public sealed unsafe class MacNotificationService : INotificationService
{
    private delegate void WillPresentDelegate(IntPtr self, IntPtr sel, IntPtr center, IntPtr notification, IntPtr completionHandler);
    private delegate void DidReceiveResponseDelegate(IntPtr self, IntPtr sel, IntPtr center, IntPtr response, IntPtr completionHandler);
    private delegate void AuthCompletionDelegate(IntPtr block, bool granted, IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjcBlockDescriptor
    {
        public ulong Reserved;
        public ulong Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjcGlobalBlock
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    private static readonly object s_staticLock = new();
    private static MacNotificationService? s_activeInstance;

    // 固定托管委托实例，防止传入 Objective-C Runtime 的函数指针被 GC 回收
    private static readonly WillPresentDelegate s_willPresentCallback = OnWillPresentNotification;
    private static readonly DidReceiveResponseDelegate s_didReceiveResponseCallback = OnDidReceiveNotificationResponse;
    private static readonly AuthCompletionDelegate s_authCompletionCallback = OnAuthorizationCompleted;

    private static IntPtr s_delegateClass;
    private static IntPtr s_authBlockPtr;

    private readonly object _syncLock = new();
    private IntPtr _delegateInstance;
    private string? _pendingTitle;
    private string? _pendingBody;
    private Action? _activeClickHandler;
    private bool _disposed;

    public void ShowNotification(string title, string body, Action? onClicked = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || _disposed)
            return;

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                if (!HasBundleIdentifier())
                {
                    // 裸二进制开发环境（无 .app Bundle）禁止调用 UNUserNotificationCenter，降级到 osascript 展示横幅
                    ShowViaAppleScript(title, body);
                    return;
                }

                ShowViaUserNotificationCenter(title, body, onClicked);
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch
        {
            // 任何平台调用异常均静默兜底，不中断后台主进程
        }
    }

    private void ShowViaUserNotificationCenter(string title, string body, Action? onClicked)
    {
        MacNativeMethods.dlopen("/System/Library/Frameworks/UserNotifications.framework/UserNotifications", 1);

        var centerCls = MacNativeMethods.objc_getClass("UNUserNotificationCenter");
        if (centerCls == IntPtr.Zero)
        {
            ShowViaAppleScript(title, body);
            return;
        }

        var currentCenterSel = MacNativeMethods.sel_registerName("currentNotificationCenter");
        var center = MacNativeMethods.objc_msgSend(centerCls, currentCenterSel);
        if (center == IntPtr.Zero)
        {
            ShowViaAppleScript(title, body);
            return;
        }

        EnsureDelegateAttached(center);

        lock (_syncLock)
        {
            _pendingTitle = title;
            _pendingBody = body;
            _activeClickHandler = onClicked;
            s_activeInstance = this;
        }

        var authBlock = EnsureAuthorizationBlock();
        if (authBlock != IntPtr.Zero)
        {
            // UNAuthorizationOptionAlert (1 << 2) | UNAuthorizationOptionSound (1 << 1) = 6
            var requestAuthSel = MacNativeMethods.sel_registerName("requestAuthorizationWithOptions:completionHandler:");
            MacNativeMethods.objc_msgSend(center, requestAuthSel, (IntPtr)6, authBlock);
        }
        else
        {
            SchedulePendingNotification(center);
        }
    }

    private void EnsureDelegateAttached(IntPtr center)
    {
        if (_delegateInstance != IntPtr.Zero)
            return;

        lock (s_staticLock)
        {
            if (s_delegateClass == IntPtr.Zero)
            {
                var existing = MacNativeMethods.objc_getClass("SlimClipNotificationDelegate");
                if (existing != IntPtr.Zero)
                {
                    s_delegateClass = existing;
                }
                else
                {
                    var nsObjectCls = MacNativeMethods.objc_getClass("NSObject");
                    var cls = MacNativeMethods.objc_allocateClassPair(nsObjectCls, "SlimClipNotificationDelegate", UIntPtr.Zero);
                    if (cls != IntPtr.Zero)
                    {
                        var willPresentSel = MacNativeMethods.sel_registerName("userNotificationCenter:willPresentNotification:withCompletionHandler:");
                        var didReceiveSel = MacNativeMethods.sel_registerName("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:");

                        MacNativeMethods.class_addMethod(cls, willPresentSel, Marshal.GetFunctionPointerForDelegate(s_willPresentCallback), "v@:@@?");
                        MacNativeMethods.class_addMethod(cls, didReceiveSel, Marshal.GetFunctionPointerForDelegate(s_didReceiveResponseCallback), "v@:@@?");
                        MacNativeMethods.objc_registerClassPair(cls);
                        s_delegateClass = cls;
                    }
                }
            }
        }

        if (s_delegateClass == IntPtr.Zero)
            return;

        var allocSel = MacNativeMethods.sel_registerName("alloc");
        var initSel = MacNativeMethods.sel_registerName("init");
        var raw = MacNativeMethods.objc_msgSend(s_delegateClass, allocSel);
        _delegateInstance = MacNativeMethods.objc_msgSend(raw, initSel);

        if (_delegateInstance != IntPtr.Zero)
        {
            var setDelegateSel = MacNativeMethods.sel_registerName("setDelegate:");
            MacNativeMethods.objc_msgSend(center, setDelegateSel, _delegateInstance);
        }
    }

    private static IntPtr EnsureAuthorizationBlock()
    {
        lock (s_staticLock)
        {
            if (s_authBlockPtr != IntPtr.Zero)
                return s_authBlockPtr;

            var blocksLib = MacNativeMethods.dlopen("/usr/lib/system/libsystem_blocks.dylib", 1);
            if (blocksLib == IntPtr.Zero)
            {
                blocksLib = MacNativeMethods.dlopen("/usr/lib/libSystem.B.dylib", 1);
            }

            if (blocksLib == IntPtr.Zero)
                return IntPtr.Zero;

            var globalBlockIsa = MacNativeMethods.dlsym(blocksLib, "_NSConcreteGlobalBlock");
            if (globalBlockIsa == IntPtr.Zero)
                return IntPtr.Zero;

            var descriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ObjcBlockDescriptor>());
            Marshal.StructureToPtr(new ObjcBlockDescriptor
            {
                Reserved = 0,
                Size = (ulong)Marshal.SizeOf<ObjcGlobalBlock>()
            }, descriptorPtr, false);

            var blockPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ObjcGlobalBlock>());
            Marshal.StructureToPtr(new ObjcGlobalBlock
            {
                Isa = globalBlockIsa,
                Flags = 1 << 28, // BLOCK_IS_GLOBAL
                Reserved = 0,
                Invoke = Marshal.GetFunctionPointerForDelegate(s_authCompletionCallback),
                Descriptor = descriptorPtr
            }, blockPtr, false);

            s_authBlockPtr = blockPtr;
            return s_authBlockPtr;
        }
    }

    private static void OnAuthorizationCompleted(IntPtr block, bool granted, IntPtr error)
    {
        if (!granted)
            return;

        MacNotificationService? instance;
        lock (s_staticLock)
        {
            instance = s_activeInstance;
        }

        if (instance == null || instance._disposed)
            return;

        try
        {
            var pool = MacNativeMethods.objc_autoreleasePoolPush();
            try
            {
                var centerCls = MacNativeMethods.objc_getClass("UNUserNotificationCenter");
                if (centerCls == IntPtr.Zero) return;
                var currentCenterSel = MacNativeMethods.sel_registerName("currentNotificationCenter");
                var center = MacNativeMethods.objc_msgSend(centerCls, currentCenterSel);
                if (center != IntPtr.Zero)
                {
                    instance.SchedulePendingNotification(center);
                }
            }
            finally
            {
                MacNativeMethods.objc_autoreleasePoolPop(pool);
            }
        }
        catch
        {
        }
    }

    private void SchedulePendingNotification(IntPtr center)
    {
        string? title;
        string? body;
        lock (_syncLock)
        {
            if (_disposed) return;
            title = _pendingTitle;
            body = _pendingBody;
        }

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            return;

        var contentCls = MacNativeMethods.objc_getClass("UNMutableNotificationContent");
        var requestCls = MacNativeMethods.objc_getClass("UNNotificationRequest");
        if (contentCls == IntPtr.Zero || requestCls == IntPtr.Zero)
            return;

        var allocSel = MacNativeMethods.sel_registerName("alloc");
        var initSel = MacNativeMethods.sel_registerName("init");
        var content = MacNativeMethods.objc_msgSend(MacNativeMethods.objc_msgSend(contentCls, allocSel), initSel);
        if (content == IntPtr.Zero)
            return;

        try
        {
            var setTitleSel = MacNativeMethods.sel_registerName("setTitle:");
            var setBodySel = MacNativeMethods.sel_registerName("setBody:");
            MacNativeMethods.objc_msgSend(content, setTitleSel, MacNativeMethods.CreateNSString(title ?? string.Empty));
            MacNativeMethods.objc_msgSend(content, setBodySel, MacNativeMethods.CreateNSString(body ?? string.Empty));

            var reqFactorySel = MacNativeMethods.sel_registerName("requestWithIdentifier:content:trigger:");
            var reqId = MacNativeMethods.CreateNSString("slimclip-update-downloaded");
            var request = MacNativeMethods.objc_msgSend(requestCls, reqFactorySel, reqId, content, IntPtr.Zero);
            if (request == IntPtr.Zero)
                return;

            var addReqSel = MacNativeMethods.sel_registerName("addNotificationRequest:withCompletionHandler:");
            MacNativeMethods.objc_msgSend(center, addReqSel, request, IntPtr.Zero);
        }
        finally
        {
            var releaseSel = MacNativeMethods.sel_registerName("release");
            MacNativeMethods.objc_msgSend_void_noargs(content, releaseSel);
        }
    }

    private static void OnWillPresentNotification(IntPtr self, IntPtr sel, IntPtr center, IntPtr notification, IntPtr completionHandler)
    {
        if (completionHandler == IntPtr.Zero)
            return;

        try
        {
            // ObjC Block_literal 布局中 invoke 函数指针固定位于 isa(8) + flags(4) + reserved(4) 之后
            var invokePtr = Marshal.ReadIntPtr(completionHandler, IntPtr.Size + 8);
            if (invokePtr != IntPtr.Zero)
            {
                // UNNotificationPresentationOptionBanner (1 << 4) | List (1 << 3) | Sound (1 << 1) = 26
                ((delegate* unmanaged[Cdecl]<IntPtr, nuint, void>)invokePtr)(completionHandler, 26);
            }
        }
        catch
        {
        }
    }

    private static void OnDidReceiveNotificationResponse(IntPtr self, IntPtr sel, IntPtr center, IntPtr response, IntPtr completionHandler)
    {
        try
        {
            Action? handler = null;
            lock (s_staticLock)
            {
                if (s_activeInstance != null && !s_activeInstance._disposed)
                {
                    lock (s_activeInstance._syncLock)
                    {
                        handler = s_activeInstance._activeClickHandler;
                    }
                }
            }

            handler?.Invoke();
        }
        catch
        {
        }
        finally
        {
            if (completionHandler != IntPtr.Zero)
            {
                try
                {
                    var invokePtr = Marshal.ReadIntPtr(completionHandler, IntPtr.Size + 8);
                    if (invokePtr != IntPtr.Zero)
                    {
                        ((delegate* unmanaged[Cdecl]<IntPtr, void>)invokePtr)(completionHandler);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private static bool HasBundleIdentifier()
    {
        var bundleCls = MacNativeMethods.objc_getClass("NSBundle");
        if (bundleCls == IntPtr.Zero)
            return false;

        var mainBundleSel = MacNativeMethods.sel_registerName("mainBundle");
        var mainBundle = MacNativeMethods.objc_msgSend(bundleCls, mainBundleSel);
        if (mainBundle == IntPtr.Zero)
            return false;

        var bundleIdSel = MacNativeMethods.sel_registerName("bundleIdentifier");
        var bundleId = MacNativeMethods.objc_msgSend(mainBundle, bundleIdSel);
        return bundleId != IntPtr.Zero;
    }

    private static void ShowViaAppleScript(string title, string body)
    {
        var escapedTitle = EscapeAppleScriptString(title);
        var escapedBody = EscapeAppleScriptString(body);
        var script = $"display notification \"{escapedBody}\" with title \"{escapedTitle}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var proc = Process.Start(psi);
    }

    internal static string EscapeAppleScriptString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    public void Dispose()
    {
        IntPtr delegateToRelease = IntPtr.Zero;
        lock (_syncLock)
        {
            _disposed = true;
            _activeClickHandler = null;
            _pendingTitle = null;
            _pendingBody = null;
            delegateToRelease = _delegateInstance;
            _delegateInstance = IntPtr.Zero;
        }

        lock (s_staticLock)
        {
            if (ReferenceEquals(s_activeInstance, this))
            {
                s_activeInstance = null;
            }
        }

        if (delegateToRelease != IntPtr.Zero && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var centerCls = MacNativeMethods.objc_getClass("UNUserNotificationCenter");
                if (centerCls != IntPtr.Zero)
                {
                    var center = MacNativeMethods.objc_msgSend(centerCls, MacNativeMethods.sel_registerName("currentNotificationCenter"));
                    if (center != IntPtr.Zero)
                    {
                        MacNativeMethods.objc_msgSend(center, MacNativeMethods.sel_registerName("setDelegate:"), IntPtr.Zero);
                    }
                }
                MacNativeMethods.objc_msgSend_void_noargs(delegateToRelease, MacNativeMethods.sel_registerName("release"));
            }
            catch
            {
            }
        }
    }
}
