using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.Platform.Windows;

public class WindowsPasteService : IPasteService
{
    private readonly uint _pngFormat = NativeMethods.RegisterClipboardFormatW("PNG");
    private readonly uint _htmlFormat = NativeMethods.RegisterClipboardFormatW("HTML Format");
    private readonly uint _rtfFormat = NativeMethods.RegisterClipboardFormatW("Rich Text Format");
    private readonly uint _preferredDropEffectFormat = NativeMethods.RegisterClipboardFormatW("Preferred DropEffect");
    private readonly uint _fileNameWFormat = NativeMethods.RegisterClipboardFormatW("FileNameW");

    public IntPtr TargetWindowHandle { get; set; }

    public async Task PasteAsync(ClipboardItem item, int delayMs)
    {
        await SetClipboardDataAsync(item);

        if (TargetWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(TargetWindowHandle))
        {
            WindowsWindowActivationService.ForceSetForegroundWindow(TargetWindowHandle);
        }

        // 资源管理器等文件宿主前台与视图焦点恢复通常需要稍充裕的调度时间
        int effectiveDelay = (item.IsFile || item.IsImage) ? Math.Max(delayMs, 100) : delayMs;
        if (effectiveDelay > 0)
        {
            await Task.Delay(effectiveDelay);
        }

        SimulateCtrlV();
    }

    public Task SetClipboardDataAsync(ClipboardItem item)
    {
        for (int retry = 0; retry < 5; retry++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    NativeMethods.EmptyClipboard();

                    if (item.IsImage && !string.IsNullOrEmpty(item.ImagePath))
                    {
                        var dataDir = SettingsService.ResolveDataDirectory();
                        var fullPath = Path.IsPathRooted(item.ImagePath) ? item.ImagePath : Path.Combine(dataDir, item.ImagePath);
                        if (File.Exists(fullPath))
                        {
                            var bytes = File.ReadAllBytes(fullPath);
                            SetImageData(bytes);

                            // 导出标准 PNG 文件并写入 CF_HDROP，支持在文件资源管理器中直接粘贴为文件
                            var exportPath = PrepareExportImageFile(dataDir, item, bytes);
                            if (!string.IsNullOrEmpty(exportPath) && File.Exists(exportPath))
                            {
                                SetDropFiles(new[] { exportPath }, setUnicodeText: false);
                            }
                        }
                    }
                    else if (item.IsFile)
                    {
                        var paths = item.ParsedFilePaths;
                        if (paths.Length > 0)
                        {
                            SetDropFiles(paths);
                        }
                    }
                    else
                    {
                        var text = item.Text ?? string.Empty;
                        SetUnicodeText(text);

                        if (!string.IsNullOrEmpty(item.HtmlContent) && _htmlFormat != 0)
                        {
                            SetGlobalBytes(_htmlFormat, Encoding.UTF8.GetBytes(item.HtmlContent + '\0'));
                        }

                        if (!string.IsNullOrEmpty(item.RtfContent) && _rtfFormat != 0)
                        {
                            SetGlobalBytes(_rtfFormat, Encoding.Default.GetBytes(item.RtfContent + '\0'));
                        }
                    }

                    break;
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }

            System.Threading.Thread.Sleep(20);
        }

        return Task.CompletedTask;
    }

    private void SetUnicodeText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        SetGlobalBytes(NativeMethods.CF_UNICODETEXT, bytes);
    }

    private void SetImageData(byte[] imageBytes)
    {
        // 1. 设置标准的 CF_DIB（Windows 原生应用如微信、QQ、Paint、Office 必需格式）
        var dibBytes = WindowsImageFormatHelper.ConvertImageToDibBytes(imageBytes);
        if (dibBytes != null && dibBytes.Length > 0)
        {
            SetGlobalBytes(NativeMethods.CF_DIB, dibBytes);
        }

        // 2. 设置标准的 PNG 注册格式（Chromium 等现代软件支持高保真与透明度）
        if (_pngFormat != 0)
        {
            var pngBytes = WindowsImageFormatHelper.ConvertImageToPngBytes(imageBytes);
            if (pngBytes != null && pngBytes.Length > 0)
            {
                SetGlobalBytes(_pngFormat, pngBytes);
            }
        }
    }

    private static void SetGlobalBytes(uint format, byte[] bytes)
    {
        var hMem = NativeMethods.GlobalAlloc(NativeMethods.GHND, (UIntPtr)bytes.Length);
        if (hMem == IntPtr.Zero) return;

        var ptr = NativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hMem);
            return;
        }

        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(hMem);
        }

        if (NativeMethods.SetClipboardData(format, hMem) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hMem);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;
        public int ptX;
        public int ptY;
        public int fNC;
        public int fWide;
    }

    private void SetDropFiles(string[] files, bool setUnicodeText = true)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var dropFiles = new DROPFILES
        {
            pFiles = (uint)Marshal.SizeOf<DROPFILES>(),
            fWide = 1
        };

        var headerBytes = new byte[Marshal.SizeOf<DROPFILES>()];
        var ptr = Marshal.AllocHGlobal(headerBytes.Length);
        try
        {
            Marshal.StructureToPtr(dropFiles, ptr, false);
            Marshal.Copy(ptr, headerBytes, 0, headerBytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        bw.Write(headerBytes);

        foreach (var file in files)
        {
            bw.Write(Encoding.Unicode.GetBytes(file));
            bw.Write((short)0); // Null terminator
        }
        bw.Write((short)0); // Double null terminator

        var buffer = ms.ToArray();
        SetGlobalBytes(NativeMethods.CF_HDROP, buffer);

        // 1. 设置 Preferred DropEffect 为 DROPEFFECT_COPY = 1（Windows Explorer 识别必须）
        if (_preferredDropEffectFormat != 0)
        {
            var pdeBytes = BitConverter.GetBytes((uint)1); // DROPEFFECT_COPY
            SetGlobalBytes(_preferredDropEffectFormat, pdeBytes);
        }

        // 2. 设置 FileNameW
        if (_fileNameWFormat != 0 && files.Length > 0)
        {
            var fnwBytes = Encoding.Unicode.GetBytes(files[0] + '\0');
            SetGlobalBytes(_fileNameWFormat, fnwBytes);
        }

        // 3. 同时提供纯文本格式，允许在文本编辑器中粘贴出文件路径（纯图片时不写入避免干扰聊天软件）
        if (setUnicodeText)
        {
            var text = string.Join(Environment.NewLine, files);
            SetUnicodeText(text);
        }
    }

    private static string? PrepareExportImageFile(string dataDir, ClipboardItem item, byte[] bytes)
    {
        try
        {
            var exportDir = Path.Combine(dataDir, "export");
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            var shortHash = !string.IsNullOrEmpty(item.ImageHash) && item.ImageHash.Length >= 4
                ? "_" + item.ImageHash[..4]
                : string.Empty;
            var fileName = $"Image_{item.CopiedAt:yyyyMMdd_HHmmss}{shortHash}.png";
            var exportPath = Path.Combine(exportDir, fileName);

            var pngBytes = WindowsImageFormatHelper.ConvertImageToPngBytes(bytes) ?? bytes;
            File.WriteAllBytes(exportPath, pngBytes);

            return exportPath;
        }
        catch
        {
            return null;
        }
    }

    private static void SimulateCtrlV()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;

        var inputs = new NativeMethods.INPUT[4];

        // Ctrl down
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_CONTROL;

        // V down
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_V;

        // V up
        inputs[2].type = NativeMethods.INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_V;
        inputs[2].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        // Ctrl up
        inputs[3].type = NativeMethods.INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_CONTROL;
        inputs[3].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            var err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"SendInput failed with error code: {err}");
        }
    }
}
