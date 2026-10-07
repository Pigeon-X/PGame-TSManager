using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PGameTSManager
{
    /// <summary>
    /// 空服时压缩 TShock 进程工作集。
    /// 不关闭端口、不暂停进程，只把暂时不用的页移出物理内存；玩家进入时由系统按需换回。
    /// </summary>
    internal static class IdleMemoryTrim
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr process);

        public static bool TryTrim(Process process)
        {
            try
            {
                if (process == null || process.HasExited) return false;
                return EmptyWorkingSet(process.Handle);
            }
            catch
            {
                return false;
            }
        }
    }
}
