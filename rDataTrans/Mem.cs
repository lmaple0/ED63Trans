#region

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace rDataTrans;

public static class Mem
{
    public class rdataString
    {
        public long offset;
        public int length;
        public string str = "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public uint BaseAddress;
        public uint AllocationBase;
        public uint AllocationProtect;
        public uint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess")]
    public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, uint lpBaseAddress, byte[] lpBuffer, int nSize,
        IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
    static extern bool WriteProcessMemory(IntPtr hProcess, uint lpBaseAddress, IntPtr lpBuffer, int nSize,
        IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory")]
    public static extern bool ReadProcessMemory(IntPtr hProcess, uint lpBaseAddress, ref uint lpBuffer, int nSize, out int lpNumberOfBytesRead);
    [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory")]
    public static extern bool ReadProcessMemory(IntPtr hProcess, uint lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);
    [DllImport("kernel32.dll")]
    public static extern void CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, uint lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern nuint VirtualQueryEx(IntPtr hProcess, uint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("KERNEL32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool VirtualProtectEx(IntPtr hProcess, uint lpAddress, IntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    static extern void ExitProcess(uint uExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private const int MEM_COMMIT = 0x00001000;
    private const int MEM_RESERVE = 0x00002000;
    private const int MEM_TOP_DOWN = 0x00100000;
    private const int MEM_RELEASE = 0x00008000;
    private const int MEM_FREE = 0x00010000;
    private const int PAGE_READWRITE = 0x04;
    private const int PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint MB_ICONERROR = 0x00000010;
    private const int WriteChunk = 1024 * 1024;

    private static IntPtr hProcess;
    public static Process? Process;
    public static bool IsDx9;

    private static readonly Dictionary<uint, (IntPtr Ptr, uint Size)> ReservedFonts = [];

    public static bool OpenED6()
    {
        var processes = Process.GetProcessesByName("ed6_win3_DX9");
        Process = null;
        if (processes.Length > 0)
        {
            Process = processes.First();
            IsDx9 = true;
            return Open(Process);
        }
        processes = Process.GetProcessesByName("ed6_win3");
        if (processes.Length > 0)
        {
            Process = processes.First();
            return Open(Process);
        }
        return false;
    }
    public static bool OpenPID(int pid)
    {
        Process = Process.GetProcessById(pid);
        if (Process == null)
            return false;
        return Open(Process);
    }
    public static void SetWindowTitle()
    {
        if (Process == null) return;
        /*The Legend of Heroes: Trails in the Sky the 3rd (DX9) - SoraVoice (Lite) 20230823*/
        var name = Process.MainWindowTitle.Replace("The Legend of Heroes: Trails in the Sky the 3rd", "英雄传说：空之轨迹the 3rd");
        IntPtr hWnd = Process.MainWindowHandle;
        SetWindowText(hWnd, name);
    }

    private static bool Open(Process p)
    {
        hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, p.Id);
        return hProcess > 0;
    }

    public static void ReserveForFont(uint px, uint size)
    {
        var ptr = Place(size, commit: false);
        if (ptr == IntPtr.Zero)
        {
            FailVisible(
                $"VirtualAllocEx reserve font{px} ({size} bytes) failed. " +
                $"GetLastError={Marshal.GetLastWin32Error()}. Free VA: {DescribeFreeVa()}");
        }
        ReservedFonts[px] = (ptr, size);
    }

    public static IntPtr TakeReservedOrAlloc(uint px, uint size)
    {
        if (ReservedFonts.Remove(px, out var slot))
        {
            uint at = FontAlloc.ToUInt32Addr(slot.Ptr);
            var committed = VirtualAllocEx(hProcess, at, slot.Size, MEM_COMMIT, PAGE_READWRITE);
            if (committed == IntPtr.Zero)
            {
                FailVisible(
                    $"MEM_COMMIT font{px} at 0x{at:X} size={slot.Size} failed, " +
                    $"GetLastError={Marshal.GetLastWin32Error()}");
            }
            return slot.Ptr;
        }

        return Alloc(size);
    }

    public static void ReleaseUnusedFontReservations()
    {
        foreach (var slot in ReservedFonts.Values)
        {
            if (slot.Ptr != IntPtr.Zero)
                VirtualFreeEx(hProcess, slot.Ptr, 0, MEM_RELEASE);
        }
        ReservedFonts.Clear();
    }

    public static IntPtr Alloc(uint size)
    {
        if (size == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "VirtualAllocEx size is 0");

        var ptr = Place(size, commit: true);
        if (ptr == IntPtr.Zero)
        {
            FailVisible(
                $"VirtualAllocEx({size} bytes) failed, GetLastError={Marshal.GetLastWin32Error()}. " +
                $"Free VA: {DescribeFreeVa()}");
        }

        return ptr;
    }

    static IntPtr Place(uint size, bool commit)
    {
        uint flags = (uint)MEM_RESERVE | (commit ? (uint)MEM_COMMIT : 0);

        // Prefer a specific free hole. TOP_DOWN in a 4K process fails first:
        // high VA is already packed with D3D/ReShade, which is how we got ERROR_NOT_ENOUGH_MEMORY=8.
        var scanned = PlaceInFreeRegion(size, flags);
        if (scanned != IntPtr.Zero)
            return scanned;

        var bottom = VirtualAllocEx(hProcess, 0, size, flags, PAGE_READWRITE);
        if (bottom != IntPtr.Zero)
            return bottom;

        return VirtualAllocEx(hProcess, 0, size, flags | (uint)MEM_TOP_DOWN, PAGE_READWRITE);
    }

    static IntPtr PlaceInFreeRegion(uint size, uint flags)
    {
        foreach (var hole in EnumerateFreeRegions())
        {
            uint start = FontAlloc.AlignUp(hole.Start, FontAlloc.AllocGranularity);
            if (start < hole.Start)
                continue;
            if (hole.End <= start || hole.End - start < size)
                continue;

            var ptr = VirtualAllocEx(hProcess, start, size, flags, PAGE_READWRITE);
            if (ptr != IntPtr.Zero)
                return ptr;
        }

        return IntPtr.Zero;
    }

    static List<(uint Start, uint End, uint Size)> EnumerateFreeRegions()
    {
        var holes = new List<(uint Start, uint End, uint Size)>();
        uint cursor = FontAlloc.AllocGranularity;
        while (cursor < 0xFFFF0000)
        {
            if (VirtualQueryEx(hProcess, cursor, out var mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                break;

            uint regionEnd = unchecked(mbi.BaseAddress + mbi.RegionSize);
            if (mbi.State == MEM_FREE && mbi.RegionSize >= FontAlloc.AllocGranularity)
                holes.Add((mbi.BaseAddress, regionEnd, mbi.RegionSize));

            if (regionEnd <= cursor)
                break;
            cursor = regionEnd;
        }

        holes.Sort((a, b) => b.Size.CompareTo(a.Size));
        return holes;
    }

    static string DescribeFreeVa()
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (var hole in EnumerateFreeRegions())
        {
            if (n == 5)
                break;
            if (n > 0)
                sb.Append("; ");
            sb.Append($"0x{hole.Start:X}={hole.Size}");
            n++;
        }
        return n == 0 ? "(none)" : sb.ToString();
    }

    [DoesNotReturn]
    public static void FailVisible(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "ED63Trans rdata.exe", MB_ICONERROR);
        }
        catch
        {
            // keep going to ExitProcess
        }

        ExitProcess(1);
        throw new InvalidOperationException(message);
    }

    public static bool ReadUint(uint addr, out uint result)
    {
        uint num = 0;
        var ret=  ReadProcessMemory(hProcess, addr, ref num, 4, out _);
        result = num;
        return ret;
    }
    public static bool Read(uint addr, out byte[] result,int len)
    {
        byte[] bytes = new byte[len];
        var ret = ReadProcessMemory(hProcess, addr,  bytes, bytes.Length,out _);
        result = bytes;
        return ret;
    }
    public static bool Write(uint addr, byte[] newData, bool changeProtect)
    {
        bool result;
        uint old = 0;
        if (changeProtect)
        {
            result = VirtualProtectEx(hProcess, addr, newData.Length, PAGE_READWRITE, out old);
            if (!result)
                return false;
        }

        result = WriteChunks(addr, newData);

        if (changeProtect)
        {
            VirtualProtectEx(hProcess, addr, newData.Length, old, out old);
        }
        return result;
    }

    static bool WriteChunks(uint addr, byte[] newData)
    {
        var pin = GCHandle.Alloc(newData, GCHandleType.Pinned);
        try
        {
            var basePtr = pin.AddrOfPinnedObject();
            for (int i = 0; i < newData.Length; i += WriteChunk)
            {
                int n = Math.Min(WriteChunk, newData.Length - i);
                if (!WriteProcessMemory(hProcess, addr + (uint)i, basePtr + i, n, IntPtr.Zero))
                    return false;
            }
            return true;
        }
        finally
        {
            pin.Free();
        }
    }
}
