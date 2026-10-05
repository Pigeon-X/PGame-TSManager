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
            // 兜底：任何未处理异常都记到 crash.log，并尽量让管理器继续运行（子进程不能被带走）
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

            PGameTSManager.MainWindow.StartAllOnLoad = e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
            window.Show();
        }
    }
}