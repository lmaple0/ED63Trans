using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AllocProbe;

internal static class Program
{
    const uint MEM_COMMIT = 0x1000;
    const uint MEM_RESERVE = 0x2000;
    const uint MEM_TOP_DOWN = 0x100000;
    const uint PAGE_READWRITE = 0x04;
    const uint MEM_RELEASE = 0x8000;
    const int PROCESS_VM_OPERATION = 0x0008;
    const int PROCESS_QUERY_INFORMATION = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool VirtualFree(IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr hObject);

    static int Main()
    {
        Console.WriteLine($"IntPtr.Size={IntPtr.Size} (want 4 = 32-bit process)");
        // Exact FONT._DA sizes from the Steam 3rd rdata folder, plus 0x20 header pad.
        // WriteFont injects every size >= 72; shrinking that set would hide VA pressure.
        uint[] sizes =
        {
            18_820_512u + 0x20,   // font72
            23_235_200u + 0x20,   // font80
            33_458_688u + 0x20,   // font96
            59_482_112u + 0x20,   // font128
            75_282_048u + 0x20,   // font144
            92_940_800u + 0x20,   // font160
            133_834_752u + 0x20,  // font192
        };
        string[] names = { "font72", "font80", "font96", "font128", "font144", "font160", "font192" };

        Console.WriteLine("--- MEM_COMMIT only (old rdata.exe) ---");
        TryFlags("COMMIT", MEM_COMMIT, sizes[^1]);

        Console.WriteLine("--- MEM_RESERVE|MEM_COMMIT|MEM_TOP_DOWN (new) sequential fonts >=72 ---");
        var ptrs = new IntPtr[sizes.Length];
        var failed = false;
        for (int i = 0; i < sizes.Length; i++)
        {
            ptrs[i] = VirtualAlloc(IntPtr.Zero, sizes[i], MEM_RESERVE | MEM_COMMIT | MEM_TOP_DOWN, PAGE_READWRITE);
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"{names[i]} size={sizes[i]} ptr=0x{ptrs[i].ToInt64():X} err={err}");
            if (ptrs[i] == IntPtr.Zero)
                failed = true;
        }

        for (int i = 0; i < ptrs.Length; i++)
        {
            if (ptrs[i] != IntPtr.Zero)
                VirtualFree(ptrs[i], 0, MEM_RELEASE);
        }

        if (failed)
        {
            Console.WriteLine("EXPOSED: 32-bit VA cannot hold every FONT._DA >=72. rdata.exe must MessageBox, not write 0x20.");
            return 2;
        }

        Console.WriteLine("32-bit empty process CAN place every font >=72. Game process still has less free VA.");

        int gameCode = ProbeGame(sizes, names);
        return failed ? 2 : gameCode;
    }

    static int ProbeGame(uint[] sizes, string[] names)
    {
        var proc = Process.GetProcessesByName("ed6_win3_DX9").FirstOrDefault()
                   ?? Process.GetProcessesByName("ed6_win3").FirstOrDefault();
        if (proc == null)
        {
            Console.WriteLine("--- game not running; skip VirtualAllocEx into ed6_win3_DX9 ---");
            return 0;
        }

        Console.WriteLine($"--- VirtualAllocEx into pid {proc.Id} {proc.ProcessName} (this is the real pressure) ---");
        var hp = OpenProcess(PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, proc.Id);
        if (hp == IntPtr.Zero)
        {
            Console.WriteLine($"OpenProcess failed err={Marshal.GetLastWin32Error()}");
            return 3;
        }

        var ptrs = new IntPtr[sizes.Length];
        var failed = false;
        for (int i = 0; i < sizes.Length; i++)
        {
            ptrs[i] = VirtualAllocEx(hp, IntPtr.Zero, sizes[i], MEM_RESERVE | MEM_COMMIT | MEM_TOP_DOWN, PAGE_READWRITE);
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"{names[i]} size={sizes[i]} ptr=0x{ptrs[i].ToInt64():X} err={err}");
            if (ptrs[i] == IntPtr.Zero)
                failed = true;
        }

        for (int i = 0; i < ptrs.Length; i++)
        {
            if (ptrs[i] != IntPtr.Zero)
                VirtualFreeEx(hp, ptrs[i], 0, MEM_RELEASE);
        }
        CloseHandle(hp);

        if (failed)
        {
            Console.WriteLine("EXPOSED: game VA cannot hold every FONT._DA >=72. New rdata.exe must MessageBox here.");
            return 2;
        }

        Console.WriteLine("Game process accepted every font >=72 this run. Failure is still possible later under fragmentation.");
        return 0;
    }

    static void TryFlags(string label, uint flags, uint size)
    {
        var p = VirtualAlloc(IntPtr.Zero, size, flags, PAGE_READWRITE);
        int err = Marshal.GetLastWin32Error();
        Console.WriteLine($"{label} font192 size={size} ptr=0x{p.ToInt64():X} err={err}");
        if (p != IntPtr.Zero)
            VirtualFree(p, 0, MEM_RELEASE);
    }
}
