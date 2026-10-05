using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PGameTSManager
{
    /// <summary>
    /// 直接把指令「敲」进目标服务器进程的控制台（CONIN$）。
    ///
    /// 为什么不用 stdin / REST：
    ///   - TShock 6.2 明确拒绝重定向 stdin：会打印
    ///     "ERROR: Input redirection is not supported, exiting the process immediately."
    ///   - REST 依赖 PGameAPI 的「现代 REST 接管」，目前 7878 没绑上
    /// 所以改成：AttachConsole 到服务器进程 → 打开它的 CONIN$ → 写入按键事件（含回车）。
    /// 服务器拿到的是「真实控制台输入」，与人工敲键盘完全等价。
    /// </summary>
    internal static class ConsoleInjector
    {
        private const ushort KEY_EVENT = 0x0001;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct KEY_EVENT_RECORD
        {
            [MarshalAs(UnmanagedType.Bool)] public bool bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public char UnicodeChar;
            public uint dwControlKeyState;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WriteConsoleInputW(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer,
            uint nLength, out uint lpNumberOfEventsWritten);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>把一行指令注入目标进程的控制台并回车。成功返回 true。</summary>
        public static bool Send(uint pid, string text, out string error)
        {
            error = "";
            IntPtr h = IntPtr.Zero;
            var attached = false;
            try
            {
                FreeConsole();                      // 先脱离自己的（WPF 本来就没有）
                if (!AttachConsole(pid)) { error = "AttachConsole 失败(错误码 " + Marshal.GetLastWin32Error() + ")"; return false; }
                attached = true;

                h = CreateFileW("CONIN$", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == new IntPtr(-1) || h == IntPtr.Zero)
                {
                    error = "打开 CONIN$ 失败(错误码 " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                var recs = new List<INPUT_RECORD>();
                foreach (var ch in (text ?? "") + "\r")
                {
                    recs.Add(Make(ch, true));
                    recs.Add(Make(ch, false));
                }
                if (!WriteConsoleInputW(h, recs.ToArray(), (uint)recs.Count, out var written))
                {
                    error = "WriteConsoleInput 失败(错误码 " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                if (written != recs.Count) { error = $"只写入 {written}/{recs.Count} 个事件"; return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                if (h != IntPtr.Zero && h != new IntPtr(-1)) { try { CloseHandle(h); } catch { } }
                if (attached) { try { FreeConsole(); } catch { } }
            }
        }

        private static INPUT_RECORD Make(char ch, bool down) => new INPUT_RECORD
        {
            EventType = KEY_EVENT,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = down,
                wRepeatCount = 1,
                wVirtualKeyCode = 0,
                wVirtualScanCode = 0,
                UnicodeChar = ch,
                dwControlKeyState = 0
            }
        };
    }
}