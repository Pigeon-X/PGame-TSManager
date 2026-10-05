using System;
using System.IO;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ExceptionProbe
{
    /// <summary>
    /// 一次性诊断插件：把进程内每一次异常（含被 catch 吞掉的）写进 firstchance.log。
    /// 目的：TShock 把启动异常交给 Program.DisplayException 后静默退出(exit 0)，日志什么都看不到。
    /// </summary>
    [ApiVersion(2, 1)]
    public class ExceptionProbe : TerrariaPlugin
    {
        public override string Name => "ExceptionProbe";
        public override Version Version => new Version(1, 0, 0, 0);
        public override string Author => "PGame-TSManager";
        public override string Description => "log first-chance exceptions";

        public ExceptionProbe(Main game) : base(game) { }

        public override void Initialize()
        {
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                try
                {
                    var log = Path.Combine(AppContext.BaseDirectory, "firstchance.log");
                    var ex = e.Exception;
                    File.AppendAllText(log,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " +
                        ex.GetType().FullName + ": " + ex.Message + "\n" +
                        ex.StackTrace + "\n\n");
                }
                catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var log = Path.Combine(AppContext.BaseDirectory, "firstchance.log");
                    File.AppendAllText(log, "=== UNHANDLED ===\n" + (e.ExceptionObject as Exception)?.ToString() + "\n\n");
                }
                catch { }
            };

            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "firstchance.log"),
                    "=== ExceptionProbe attached " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n");
            }
            catch { }
        }
    }
}