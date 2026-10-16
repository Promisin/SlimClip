using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace SlimClip.Tests;

public class DropFilesTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;
        public int ptX;
        public int ptY;
        public int fNC;
        public int fWide;
    }

    [Fact]
    public void DropFilesStructure_MatchesWin32StandardSize()
    {
        // Win32 tagDROPFILES size must be exactly 20 bytes (DWORD + POINT + BOOL + BOOL)
        Assert.Equal(20, Marshal.SizeOf<DROPFILES>());
    }

    [Fact]
    public void DropFilesBuffer_CorrectlyFormattedWithNullTerminators()
    {
        string[] files = ["C:\\test\\file1.txt", "D:\\folder\\file2.png"];

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

        // 1. Verify header offset
        uint pFiles = BitConverter.ToUInt32(buffer, 0);
        Assert.Equal(20u, pFiles);

        // 2. Read back string list
        var readFiles = new List<string>();
        int offset = (int)pFiles;
        var current = new StringBuilder();

        while (offset < buffer.Length - 1)
        {
            char c = (char)BitConverter.ToInt16(buffer, offset);
            offset += 2;
            if (c == '\0')
            {
                if (current.Length == 0)
                {
                    // Double null terminator reached
                    break;
                }
                readFiles.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        Assert.Equal(2, readFiles.Count);
        Assert.Equal(files[0], readFiles[0]);
        Assert.Equal(files[1], readFiles[1]);
    }
}
