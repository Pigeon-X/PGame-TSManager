using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace PGameTSManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        /// <summary>
        /// 给管理器分配一个「隐藏控制台」。
        /// 目的：服务器子进程继承这个控制台后，stdin 是真正的控制台输入（不会 EOF），
        /// TShock 6.2 就不会因为 stdin EOF 而自己退出；而这个控制台窗口是隐藏的，看不到。
        /// </summary>
        private static void EnsureHiddenConsole()
        {
            try
            {
                if (GetConsoleWindow() == IntPtr.Zero)
                {
                    AllocConsole();
                }
                var h = GetConsoleWindow();
                if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE);
            }
            catch { }
        }
        public App()
        {
            // ★ 关键：默认 OnLastWindowClose 会让管理器在窗口被关/引用丢失时整个退出，
            //   一并把拉起的三个服务器带走（表现为“服务器 40 秒自退、exit 0”）。
            //   改成显式关停：只有用户关窗口或主动退出才结束进程。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (_, e) =>
            {
                LogCrash("DispatcherUnhandledException", e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogCrash("UnhandledException", e.ExceptionObject as Exception);
        }

        private static void LogCrash(string kind, Exception? ex)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + kind);
                sb.AppendLine(ex?.ToString() ?? "(null)");
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any(a => string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(SelfCheck.Run());
                return;
            }

            if (e.Args.Any(a => string.Equals(a, "--syncplugins", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(SelfCheck.SyncPlugins());
                return;
            }

            if (e.Args.Any(a => string.Equals(a, "--nowindow", StringComparison.OrdinalIgnoreCase)))
            {
                // 只影响“服务器”窗口，不影响管理器自身窗口：管理器永远可视化显示
                ManagerConfig.ShowWindowOverride = false;
            }

            EnsureHiddenConsole();

            PGameTSManager.MainWindow.StartAllOnLoad =
                e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
            window.Closing += (_, _) =>
            {
                LogCrash("MainWindowClosing", new Exception("stack:\n" + Environment.StackTrace));
                Current.Shutdown();
            };
            window.Show();

            // 强制可视化：正常状态 + 显示在任务栏 + 提到前台
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = true;
            window.Activate();
            try
            {
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();
            }
            catch { }
        }
    }
}