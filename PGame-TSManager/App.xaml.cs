using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace PGameTSManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
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

            PGameTSManager.MainWindow.StartAllOnLoad =
                e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
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