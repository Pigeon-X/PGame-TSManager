using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PGameTSManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (_, e) => { LogCrash("DispatcherUnhandledException", e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("UnhandledException", e.ExceptionObject as Exception);
        }

        private static void LogCrash(string kind, Exception? ex)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + kind + "\n" + (ex?.ToString() ?? "(null)") + "\n\n", new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>
        /// 配色：跟随 TShock 控制台（黑底 + 灰字 + 黄 INFO + 红 ERROR），
        /// 并根据 Windows 的「应用主题」自动切深色/浅色。
        /// </summary>
        private static void ApplyTheme()
        {
            var light = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                light = Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 0) == 1;
            }
            catch { }

            var r = Current.Resources;
            void Set(string k, string hex) => r[k] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

            // TShock 控制台的三种主色，浅色深色都用它
            Set("ConsoleBg", "#000000");
            Set("Ok", "#66BB6A");
            Set("Err", "#FF5252");
            Set("Warn", "#FFB74D");
            Set("Info", "#FFD23F");   // TShock ConsoleInfo 黄
            Set("Cmd", "#4FC3F7");

            if (light)
            {
                Set("Bg", "#F3F3F3");
                Set("Panel", "#FFFFFF");
                Set("PanelHi", "#E8E8E8");
                Set("Border", "#CFCFCF");
                Set("Text", "#1B1B1B");
                Set("TextDim", "#6A6A6A");
                Set("Accent", "#C8A02A");   // 深一点的黄，浅底上看得清
                Set("Accent2", "#E0B93A");
                Set("BtnText", "#FFFFFF");
            }
            else
            {
                Set("Bg", "#0B0B0B");
                Set("Panel", "#151515");
                Set("PanelHi", "#1F1F1F");
                Set("Border", "#303030");
                Set("Text", "#D6D6D6");
                Set("TextDim", "#8A8A8A");
                Set("Accent", "#FFD23F");
                Set("Accent2", "#FFE07A");
                Set("BtnText", "#101010");
            }

            // ★ 控制台配色：保持原来那套（彩色、区分度高），不跟随窗口主题
            //   时间戳/普通=浅灰  INFO=青  插件名=紫  警告=琥珀  错误=红  成功=绿  指令=蓝
            r["LogNormal"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6E6EE"));
            r["LogInfo"]   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4FC3F7"));
            r["LogPlugin"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
            r["LogWarn"]   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            r["LogError"]  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            r["LogOk"]     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            r["LogCmd"]    = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4C8DF6"));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any(a => string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase)))
            { Shutdown(SelfCheck.Run()); return; }

            if (e.Args.Any(a => string.Equals(a, "--syncplugins", StringComparison.OrdinalIgnoreCase)))
            { Shutdown(SelfCheck.SyncPlugins()); return; }

            ApplyTheme();

            if (e.Args.Any(a => string.Equals(a, "--nowindow", StringComparison.OrdinalIgnoreCase)))
                ManagerConfig.ShowWindowOverride = false;

            PGameTSManager.MainWindow.StartAllOnLoad =
                e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
            window.Closing += (_, _) => { LogCrash("MainWindowClosing", null); Current.Shutdown(); };
            window.Show();
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = true;
            window.Activate();
            try { window.Topmost = true; window.Topmost = false; window.Focus(); } catch { }
        }
    }
}