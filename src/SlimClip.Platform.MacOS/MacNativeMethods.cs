using System;
using System.Runtime.InteropServices;

namespace SlimClip.Platform.MacOS;

internal static class MacNativeMethods
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServicesLib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // --- Objective-C Runtime ---
    [DllImport(ObjCLib, EntryPoint = "objc_getClass")]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLib, EntryPoint = "sel_registerName")]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport("libSystem.dylib")]
    public static extern IntPtr dlopen(string path, int mode);

    [DllImport("libSystem.dylib")]
    public static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static IntPtr _cfRunLoopCommonModes;
    public static IntPtr GetCFRunLoopCommonModes()
    {
        if (_cfRunLoopCommonModes == IntPtr.Zero)
        {
            var cfLib = dlopen("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", 1);
            if (cfLib != IntPtr.Zero)
            {
                var ptr = dlsym(cfLib, "kCFRunLoopCommonModes");
                if (ptr != IntPtr.Zero)
                {
                    _cfRunLoopCommonModes = Marshal.ReadIntPtr(ptr);
                }
            }
        }
        return _cfRunLoopCommonModes;
    }

    // --- Carbon Event Manager (Global Hotkeys) ---
    private const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    public const uint kEventClassKeyboard = 0x6b657962; // 'keyb'
    public const uint kEventHotKeyPressed = 5;
    public const uint typeEventHotKeyID = 0x686b6964; // 'hkid'
    public const uint kEventParamDirectObject = 0x2d2d2d2d; // '----'

    public const uint carbonCmdKey = 0x0100;
    public const uint carbonShiftKey = 0x0200;
    public const uint carbonAlphaLock = 0x0400;
    public const uint carbonOptionKey = 0x0800;
    public const uint carbonControlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int EventHandlerDelegate(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(CarbonLib)]
    public static extern IntPtr GetApplicationEventTarget();

    [DllImport(CarbonLib)]
    public static extern int InstallEventHandler(IntPtr inTarget, EventHandlerDelegate inHandler, uint inNumTypes, [In] EventTypeSpec[] inList, IntPtr inUserData, out IntPtr outHandlerRef);

    [DllImport(CarbonLib)]
    public static extern int RegisterEventHotKey(uint inHotKeyCode, uint inHotKeyModifiers, EventHotKeyID inHotKeyID, IntPtr inTarget, uint inOptions, out IntPtr outRef);

    [DllImport(CarbonLib)]
    public static extern int UnregisterEventHotKey(IntPtr inHotKey);

    [DllImport(CarbonLib)]
    public static extern int RemoveEventHandler(IntPtr inHandlerRef);

    [DllImport(CarbonLib)]
    public static extern int GetEventParameter(IntPtr inEvent, uint inName, uint inDesiredType, IntPtr outActualType, uint inBufferSize, out uint outActualSize, ref EventHotKeyID outData);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public CGRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_noargs(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_double(IntPtr receiver, IntPtr selector, double arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_CGRect(IntPtr receiver, IntPtr selector, CGRect rect);

    // --- CoreGraphics CGEvent ---
    public const uint kCGHIDEventTap = 0;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;
    public const ushort kVK_ANSI_V = 0x09;

    [DllImport(CoreGraphicsLib)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport(CoreGraphicsLib)]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(ObjCLib)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjCLib)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(CoreGraphicsLib)]
    public static extern void CGEventPost(uint tap, IntPtr @event);

    [DllImport(CoreGraphicsLib)]
    public static extern void CFRelease(IntPtr cf);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(CoreGraphicsLib)]
    public static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphicsLib)]
    public static extern CGPoint CGEventGetLocation(IntPtr @event);

    public delegate IntPtr CGEventTapCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr refcon);

    [DllImport(CoreGraphicsLib)]
    public static extern IntPtr CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        CGEventTapCallback callback,
        IntPtr refcon);

    [DllImport(CoreGraphicsLib)]
    public static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport(CoreGraphicsLib)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, IntPtr order);

    [DllImport(CoreGraphicsLib)]
    public static extern IntPtr CFRunLoopGetMain();

    [DllImport(CoreGraphicsLib)]
    public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreGraphicsLib)]
    public static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreGraphicsLib)]
    public static extern long CGEventGetIntegerValueField(IntPtr @event, int field);

    [DllImport(CoreGraphicsLib)]
    public static extern ulong CGEventGetFlags(IntPtr @event);

    public const int kCGKeyboardEventKeycode = 9;
    public const uint kCGEventKeyDown = 10;
    public const ulong kCGEventFlagMaskShift = 0x00020000;
    public const ulong kCGEventFlagMaskControl = 0x00040000;
    public const ulong kCGEventFlagMaskAlternate = 0x00080000;

    // --- Accessibility API ---
    [DllImport(ApplicationServicesLib)]
    public static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServicesLib)]
    public static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServicesLib)]
    public static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServicesLib)]
    public static extern bool AXValueGetValue(IntPtr value, int type, out CGPoint valuePtr);

    public const int kAXValueCGPointType = 1;

    // Helper to create NSString
    public static IntPtr CreateNSString(string str)
    {
        var cls = objc_getClass("NSString");
        var sel = sel_registerName("stringWithUTF8String:");
        var bytes = System.Text.Encoding.UTF8.GetBytes(str + '\0');
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return objc_msgSend(cls, sel, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // Helper to read NSString
    public static string? ReadNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var sel = sel_registerName("UTF8String");
        var utf8Ptr = objc_msgSend(nsString, sel);
        return Marshal.PtrToStringUTF8(utf8Ptr);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MacRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_stret_rect(out MacRect ret, IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern MacRect objc_msgSend_arm64_rect(IntPtr receiver, IntPtr selector);

    public static MacRect GetScreenFrame(IntPtr screen)
    {
        var sel = sel_registerName("frame");
        return GetRect(screen, sel);
    }

    public static MacRect GetScreenVisibleFrame(IntPtr screen)
    {
        var sel = sel_registerName("visibleFrame");
        return GetRect(screen, sel);
    }

    private static MacRect GetRect(IntPtr receiver, IntPtr selector)
    {
        try
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return objc_msgSend_arm64_rect(receiver, selector);
            }
            else
            {
                objc_msgSend_stret_rect(out var rect, receiver, selector);
                return rect;
            }
        }
        catch
        {
            return default;
        }
    }
}
