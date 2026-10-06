using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PGameTSManager
{
    /// <summary>
    /// 原生窗口外框美化（全部是尽力而为，失败静默忽略）：
    ///   - Win10 1809+ ：标题栏跟随深色主题（不再是刺眼的白条）
    ///   - Win11 22000+：圆角窗口 + 标题栏/边框着色（Server 2022 上会自动跳过）
    /// </summary>
    internal static class WindowChromeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;   // 1809 ~ 1909
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;       // 2004+
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window, bool dark, int captionBgr = -1, int borderBgr = -1)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                var flag = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int)) != 0)
                {
                    var old = dark ? 1 : 0;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref old, sizeof(int));
                }

                var round = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

                if (captionBgr >= 0) { var c = captionBgr; DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref c, sizeof(int)); }
                if (borderBgr >= 0) { var b = borderBgr; DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref b, sizeof(int)); }
            }
            catch { }
        }

        /// <summary>#RRGGBB → DWM 需要的 COLORREF（0x00BBGGRR）</summary>
        public static int Bgr(string hex)
        {
            try
            {
                var s2 = hex.TrimStart('#');
                var r = Convert.ToInt32(s2.Substring(0, 2), 16);
                var g = Convert.ToInt32(s2.Substring(2, 2), 16);
                var b = Convert.ToInt32(s2.Substring(4, 2), 16);
                return (b << 16) | (g << 8) | r;
            }
            catch { return -1; }
        }
    }
}