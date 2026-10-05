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

    /// <summary>字符 -> 虚拟键码（很多控制台会忽略 VK=0 的事件，必须填）</summary>
    private static ushort VkOf(char c)
    {
        if (c >= 'a' && c <= 'z') return (ushort)(0x41 + (c - 'a'));
        if (c >= 'A' && c <= 'Z') return (ushort)(0x41 + (c - 'A'));
        if (c >= '0' && c <= '9') return (ushort)(0x30 + (c - '0'));
        switch (c)
        {
            case ' ': return 0x20;
            case '\r': return 0x0D;
            case '\n': return 0x0D;
            case '/': return 0xBF;   // VK_OEM_2
            case '\\': return 0xDC;  // VK_OEM_5
            case '-': return 0xBD;   // VK_OEM_MINUS
            case '=': return 0xBB;   // VK_OEM_PLUS
            case '.': return 0xBE;   // VK_OEM_PERIOD
            case ',': return 0xBC;   // VK_OEM_COMMA
            case ';': return 0xBA;   // VK_OEM_1
            case '\'': return 0xDE;  // VK_OEM_7
            default: return 0;
        }
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
                    list.Add(new INPUT_RECORD { EventType = 1, KeyEvent = new KEY_EVENT_RECORD { bKeyDown = down, wRepeatCount = 1, wVirtualKeyCode = VkOf(ch), UnicodeChar = ch } });

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