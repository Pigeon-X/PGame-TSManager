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
        /// <summary>Windows 的「应用主题」是不是浅色。</summary>
        private static bool IsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 0) == 1;
            }
            catch { return false; }
        }

        private static void ApplyTheme()
        {
            var light = IsLightTheme();

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
                Set("Bg", "#F6F5FB");
                Set("Panel", "#FFFFFF");
                Set("PanelHi", "#ECEAF6");
                Set("Border", "#D5D1E8");
                Set("Text", "#1C1A2B");
                Set("TextDim", "#6E6A88");
                Set("InputBg", "#FFFFFF");          // 输入框比面板更亮
                Set("Accent", "#7C3AED");           // 亮紫（主色）
                Set("Accent2", "#A855F7");
                Set("AccentBlue", "#2563EB");
                Set("AccentBorder", "#C4B5FD");
                Set("AccentText", "#FFFFFF");
                Set("ScrollThumb", "#B9A5F0");
                Set("ScrollThumbHot", "#7C3AED");
                Set("BtnText", "#FFFFFF");
            }
            else
            {
                Set("Bg", "#0E0D16");
                Set("Panel", "#171625");
                Set("PanelHi", "#221F35");
                Set("Border", "#342F4D");
                Set("Text", "#E8E6F5");
                Set("TextDim", "#9B97B8");
                Set("InputBg", "#26223A");          // ★ 输入框不再是暗底（比面板明显亮）
                Set("Accent", "#A855F7");           // ★ 亮紫
                Set("Accent2", "#C084FC");
                Set("AccentBlue", "#5B9DFF");
                Set("AccentBorder", "#4C3E77");
                Set("AccentText", "#FFFFFF");
                Set("ScrollThumb", "#6D5AA8");
                Set("ScrollThumbHot", "#A855F7");
                Set("BtnText", "#101014");
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

        private static System.Windows.Forms.NotifyIcon? _tray;

        /// <summary>已确认退出后置位：避免 Shutdown 再次触发 Closing 造成二次弹窗。</summary>
        private static bool _exiting;

        /// <summary>托盘图标：最小化到托盘后可从托盘恢复。</summary>
        private static void SetupTrayIcon(Window window)
        {
            try
            {
                _tray = new System.Windows.Forms.NotifyIcon
                {
                    Text = "PGame-TSManager",
                    Visible = false,
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)
                };
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("显示管理器", null, (_, _) => RestoreFromTray(window));
                menu.Items.Add("退出", null, (_, _) => { _exiting = true; LogCrash("TrayExit", null); Current.Shutdown(); });
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += (_, _) => RestoreFromTray(window);
            }
            catch (Exception ex) { LogCrash("SetupTrayIcon", ex); }
        }

        private static void RestoreFromTray(Window window)
        {
            try
            {
                if (_tray != null) _tray.Visible = false;
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
                window.Topmost = true; window.Topmost = false;
            }
            catch { }
        }

        /// <summary>
        /// 点 × 时询问：最小化到托盘 / 关闭 / 取消。
        /// 选"关闭"还要再确认一次（是与否）。
        /// </summary>
        private static void OnMainWindowClosing(Window window, System.ComponentModel.CancelEventArgs e)
        {
            if (_exiting) { e.Cancel = false; return; }   // 已确认退出，直接放行
            e.Cancel = true;   // 一律先拦下，由我们决定
            try
            {
                var choice = System.Windows.MessageBox.Show(
                    window,
                    "点 × 了：要缩小到托盘，还是关闭 PGame-TSManager？\n\n" +
                    "【是】= 缩小到托盘（后台继续管理服务器）\n" +
                    "【否】= 关闭程序\n" +
                    "【取消】= 什么都不做",
                    "PGame-TSManager",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);

                if (choice == MessageBoxResult.Yes)
                {
                    if (_tray != null)
                    {
                        _tray.Visible = true;
                        try { _tray.ShowBalloonTip(2000, "PGame-TSManager", "已缩小到托盘，双击图标可恢复。", System.Windows.Forms.ToolTipIcon.Info); } catch { }
                    }
                    window.Hide();
                    return;
                }

                if (choice == MessageBoxResult.No)
                {
                    var confirm = System.Windows.MessageBox.Show(
                        window,
                        "确定要关闭 PGame-TSManager 吗？\n\n正在运行的服务器会一起被关闭。",
                        "确认关闭",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (confirm == MessageBoxResult.Yes)
                    {
                        _exiting = true;
                        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
                        LogCrash("MainWindowClosing", null);
                        e.Cancel = false;
                        Current.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                LogCrash("OnMainWindowClosing", ex);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any(a => string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase)))
            { Shutdown(SelfCheck.Run()); return; }

            if (e.Args.Any(a => string.Equals(a, "--syncplugins", StringComparison.OrdinalIgnoreCase)))
            { Shutdown(SelfCheck.SyncPlugins()); return; }

            if (e.Args.Any(a => string.Equals(a, "--send", StringComparison.OrdinalIgnoreCase)))
            { Shutdown(Cli.Send(e.Args)); return; }

            ApplyTheme();

            if (e.Args.Any(a => string.Equals(a, "--nowindow", StringComparison.OrdinalIgnoreCase)))
                ManagerConfig.ShowWindowOverride = false;

            PGameTSManager.MainWindow.StartAllOnLoad =
                e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
            SetupTrayIcon(window);
            // 原生外框跟随主题：深色标题栏 + 紫色描边（Win11 上还会变圆角）
            window.SourceInitialized += (_, _) =>
            {
                var isLight = IsLightTheme();
                WindowChromeHelper.Apply(window, !isLight,
                    WindowChromeHelper.Bgr(isLight ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(isLight ? "#A855F7" : "#6D28D9"));
            };
            window.Closing += (_, e) => OnMainWindowClosing(window, e);
            window.Show();
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = true;
            window.Activate();
            try { window.Topmost = true; window.Topmost = false; window.Focus(); } catch { }
        }
    }
}