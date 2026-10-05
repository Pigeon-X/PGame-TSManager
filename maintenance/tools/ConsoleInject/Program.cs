using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 用法: ConsoleInject <PID> <指令>
// 把指令「敲」进目标进程的控制台（AttachConsole + WriteConsoleInput）。
internal static class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct KEY_EVENT_RECORD { [MarshalAs(UnmanagedType.Bool)] public bool bKeyDown; public ushort wRepeatCount, wVirtualKeyCode, wVirtualScanCode; public char UnicodeChar; public uint dwControlKeyState; }
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD { [FieldOffset(0)] public ushort EventType; [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WriteConsoleInputW(IntPtr h, INPUT_RECORD[] b, uint n, out uint w);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);

    private static void Log(string s)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "inject_result.txt"), s + "\n"); } catch { }
        Console.WriteLine(s);
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: ConsoleInject <PID> <指令>"); return 1; }
        var pid = uint.Parse(args[0]);
        var text = string.Join(" ", args, 1, args.Length - 1);

        IntPtr h = IntPtr.Zero;
        var attached = false;
        try
        {
            FreeConsole();
            if (!AttachConsole(pid)) { Log("AttachConsole 失败 err=" + Marshal.GetLastWin32Error()); return 2; }
            attached = true;
            h = CreateFileW("CONIN$", 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero) { Log("打开 CONIN$ 失败 err=" + Marshal.GetLastWin32Error()); return 3; }

            var list = new List<INPUT_RECORD>();
            foreach (var ch in text + "\r")
                foreach (var down in new[] { true, false })
                    list.Add(new INPUT_RECORD { EventType = 1, KeyEvent = new KEY_EVENT_RECORD { bKeyDown = down, wRepeatCount = 1, UnicodeChar = ch } });

            if (!WriteConsoleInputW(h, list.ToArray(), (uint)list.Count, out var written))
            { Log("WriteConsoleInput 失败 err=" + Marshal.GetLastWin32Error()); return 4; }
            Log("OK 写入 " + written + " 个按键事件，指令: " + text);
            return 0;
        }
        finally
        {
            if (h != IntPtr.Zero && h != new IntPtr(-1)) CloseHandle(h);
            if (attached) FreeConsole();
        }
    }
}