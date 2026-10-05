using System;
using System.Linq;
using System.Windows;

namespace PGameTSManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 无界面自检：不打开主窗口，结果写入 selfcheck.txt。
            if (e.Args.Any(a => string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(SelfCheck.Run());
                return;
            }

            // 按每服 config.json 从总插件库同步 ServerPlugins（不启动服务器）。
            if (e.Args.Any(a => string.Equals(a, "--syncplugins", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(SelfCheck.SyncPlugins());
                return;
            }

            var window = new MainWindow();
            window.Show();
        }
    }
}