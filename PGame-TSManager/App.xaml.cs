using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        private const string SingleInstanceMutexName = @"Local\Pigeon.PGameTSManager.SingleInstance";
        private const string SingleInstanceShowEventName = @"Local\Pigeon.PGameTSManager.ShowWindow";
        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _showWindowEvent;
        private static RegisteredWaitHandle? _showWindowRegistration;
        private static bool _showWindowRequested;

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
        public static bool IsLightTheme()
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
            // 渐变里的 GradientStop.Color 需要 Color 本身，给 Brush 会抛 XamlParseException
            void SetColor(string k, string hex) => r[k] = (Color)ColorConverter.ConvertFromString(hex);

            // TShock 控制台的三种主色，浅色深色都用它
            Set("ConsoleBg", "#000000");
            Set("Ok", "#66BB6A");
            Set("Err", "#FF5252");
            Set("Warn", "#FFB74D");
            Set("Info", "#FFD23F");   // TShock ConsoleInfo 黄
            Set("Cmd", "#4FC3F7");

            // ★ 液态玻璃配色：窗口底色是「极光渐变」，上面的面板都是半透明玻璃
            if (light)
            {
                SetColor("Backdrop1", "#EFE9FF");
                SetColor("Backdrop2", "#E6F0FF");
                SetColor("Backdrop3", "#F7EDFF");
                SetColor("Blob1", "#59C4B5FD");
                SetColor("Blob2", "#4F93C5FD");
                Set("GlassBg", "#B3FFFFFF");
                Set("GlassBorder", "#4D7C6BC8");
                Set("GlassHi", "#80FFFFFF");
                Set("GlassPanel", "#99FFFFFF");
                Set("GlassPanelHi", "#E6FFFFFF");
                Set("ConsoleBg", "#F207070E");
                Set("AccentBorder", "#66A855F7");
            }
            else
            {
                SetColor("Backdrop1", "#17123A");
                SetColor("Backdrop2", "#0D1030");
                SetColor("Backdrop3", "#1C1138");
                SetColor("Blob1", "#8C7C3AED");
                SetColor("Blob2", "#732563EB");
                Set("GlassBg", "#1FFFFFFF");
                Set("GlassBorder", "#3DFFFFFF");
                Set("GlassHi", "#1FFFFFFF");
                Set("GlassPanel", "#14FFFFFF");
                Set("GlassPanelHi", "#24FFFFFF");
                Set("ConsoleBg", "#CC05050C");
                Set("AccentBorder", "#59A855F7");
            }

            if (light)
            {
                Set("Bg", "#F6F5FB");
                Set("Panel", "#FFFFFF");
                Set("PanelHi", "#ECEAF6");
                Set("Border", "#D5D1E8");
                Set("Text", "#1C1A2B");
                Set("TextDim", "#6E6A88");
                Set("InputBg", "#FFFFFF");          // 浅色主题：纯白输入框
                Set("Accent", "#7C3AED");           // 亮紫（主色）
                Set("Accent2", "#A855F7");
                Set("AccentBlue", "#2563EB");
                Set("AccentBorder", "#C4B5FD");
                Set("AccentText", "#FFFFFF");
                Set("ScrollThumb", "#99A78BEA");
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
                Set("InputBg", "#3A3159");          // ★ 亮紫玻璃底：明显更亮 + 带主色
                Set("Accent", "#A855F7");           // ★ 亮紫
                Set("Accent2", "#C084FC");
                Set("AccentBlue", "#5B9DFF");
                Set("AccentBorder", "#4C3E77");
                Set("AccentText", "#FFFFFF");
                Set("ScrollThumb", "#66C4B5FD");
                Set("ScrollThumbHot", "#A855F7");
                Set("BtnText", "#101014");
            }

            // ★ 控制台配色：保持原来那套（彩色、区分度高），不跟随窗口主题
            //   TShock 标准输出=金色  Server API/提示=青  插件名=紫
            //   警告=琥珀  错误=红  成功=绿  管理器指令=蓝
            r["LogNormal"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD23F"));
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
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PGame-TSManager.exe"))
                };
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("显示管理器", null, (_, _) => RestoreFromTray(window));
                menu.Items.Add("退出", null, (_, _) => ExitFromTray());
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

        private static Window? _window;

        /// <summary>从托盘恢复主窗口（托盘菜单/双击图标调用）。</summary>
        public static void ShowFromTray()
        {
            var w = _window;
            if (w == null) return;
            RestoreFromTray(w);
        }

        /// <summary>
        /// 更新托盘图标的悬浮提示（显示各服状态与在线人数）。
        /// WinForms 的 NotifyIcon.Text 最多 63 个字符，超了会抛异常，这里先截断。
        /// </summary>
        public static void UpdateTrayTip(string text)
        {
            try
            {
                if (_tray == null) return;
                var t = (text ?? "").Replace("\r", "").Replace("\n", "  |  ").Trim();
                if (t.Length == 0) t = "PGame-TSManager";
                if (t.Length > 62) t = t.Substring(0, 59) + "...";
                _tray.Text = t;
            }
            catch { }
        }

        /// <summary>替换托盘右键菜单（主窗口按当前服务器列表生成）。</summary>
        public static void SetTrayMenu(System.Windows.Forms.ContextMenuStrip menu)
        {
            try
            {
                if (_tray == null || menu == null) return;
                var old = _tray.ContextMenuStrip;
                _tray.ContextMenuStrip = menu;
                old?.Dispose();
            }
            catch { }
        }

        /// <summary>托盘菜单里「退出」用。</summary>
        public static void ExitFromTray()
        {
            ShutdownApplication();
        }

        private static void StopManagedServers()
        {
            if (_window is not MainWindow mainWindow) return;
            foreach (var container in mainWindow.Containers.ToList())
            {
                try { container.StopForExit(TimeSpan.FromSeconds(8)); }
                catch { }
            }
        }

        /// <summary>
        /// 统一退出：先停掉所有受管 TShock 子进程，再释放托盘，最后确保 TSM 进程退出。
        /// </summary>
        private static void ShutdownApplication()
        {
            if (_exiting) return;
            _exiting = true;
            try
            {
                StopManagedServers();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            }
            catch { }
            DisposeSingleInstance();
            try { Current.Shutdown(); } catch { }
            try { Environment.Exit(0); } catch { }
        }

        private static bool EnsureSingleInstance()
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
                if (createdNew)
                {
                    _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceShowEventName);
                    _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
                        _showWindowEvent,
                        (_, timedOut) => { if (!timedOut) RequestShowExistingWindow(); },
                        null,
                        Timeout.Infinite,
                        executeOnlyOnce: false);
                    return true;
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                // First instance owns the mutex; ask it to restore its window.
                for (var i = 0; i < 20; i++)
                {
                    try
                    {
                        using var showEvent = EventWaitHandle.OpenExisting(SingleInstanceShowEventName);
                        showEvent.Set();
                        return false;
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        Thread.Sleep(50);
                    }
                    catch
                    {
                        break;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                // Never prevent startup merely because the single-instance gate failed.
                LogCrash("EnsureSingleInstance", ex);
                return true;
            }
        }

        private static void RequestShowExistingWindow()
        {
            try
            {
                var app = Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _showWindowRequested = true;
                    if (_window == null) return;
                    _showWindowRequested = false;
                    RestoreFromTray(_window);
                }));
            }
            catch { }
        }

        private static void DisposeSingleInstance()
        {
            try { _showWindowRegistration?.Unregister(null); } catch { }
            _showWindowRegistration = null;

            _showWindowEvent?.Dispose();
            _showWindowEvent = null;

            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
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
                var choice = new ChoiceDialog(
                    "PGame-TSManager",
                    "点 × 了：要缩小到托盘，还是关闭程序？\n缩小到托盘后，服务器会继续在后台运行。",
                    "缩小到托盘",
                    "关闭程序",
                    "取消") { Owner = window };
                choice.ShowDialog();

                if (choice.Result == ChoiceDialogResult.Primary)
                {
                    if (_tray != null)
                    {
                        _tray.Visible = true;
                        try { _tray.ShowBalloonTip(2000, "PGame-TSManager", "已缩小到托盘，双击图标可恢复。", System.Windows.Forms.ToolTipIcon.Info); } catch { }
                    }
                    window.Hide();
                    return;
                }

                if (choice.Result == ChoiceDialogResult.Secondary)
                {
                    var confirm = new ChoiceDialog(
                        "确认关闭",
                        "确定要关闭 PGame-TSManager 吗？\n正在运行的服务器会一起被关闭。",
                        "关闭",
                        "",
                        "取消",
                        primaryDanger: true) { Owner = window };
                    confirm.ShowDialog();
                    if (confirm.Result == ChoiceDialogResult.Primary)
                    {
                        e.Cancel = false;
                        ShutdownApplication();
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

            if (!EnsureSingleInstance())
            {
                Shutdown(0);
                return;
            }

            ApplyTheme();

            if (e.Args.Any(a => string.Equals(a, "--nowindow", StringComparison.OrdinalIgnoreCase)))
                ManagerConfig.ShowWindowOverride = false;

            PGameTSManager.MainWindow.StartAllOnLoad =
                e.Args.Any(a => string.Equals(a, "--startall", StringComparison.OrdinalIgnoreCase));

            MainWindow window;
            try
            {
                window = new MainWindow();
            }
            catch (Exception ex)
            {
                // 宁可明确报错退出，也不要留下“进程在、窗口没有”的僵死状态
                LogCrash("MainWindowCtor", ex);
                MessageBox.Show("界面初始化失败：\n\n" + ex.Message, "PGame-TSManager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            _window = window;
            SetupTrayIcon(window);
            // 原生外框跟随主题：深色标题栏 + 紫色描边（Win11 上还会变圆角）
            window.SourceInitialized += (_, _) =>
            {
                if (window.WindowStyle == WindowStyle.None) return;
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
            if (_showWindowRequested)
            {
                _showWindowRequested = false;
                RestoreFromTray(window);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DisposeSingleInstance();
            base.OnExit(e);
        }
    }
}
