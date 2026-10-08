using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGameTSManager
{
    /// <summary>
    /// 告警：把「服务器掉线 / 反复崩溃 / 日志出现致命异常」发到配置里指定的群。
    ///
    /// Core 不内置任何私人路径与群号：上报脚本和群号全部来自 config.json 的
    /// alertScript / alertGroupId / alertGroupIds；未配置就等于关闭，发送失败静默处理，
    /// 绝不影响管理器本身。
    ///
    /// 脚本契约（外部实现只需满足这个约定）：
    ///   &lt;script&gt; -Text "&lt;告警正文&gt;" -GroupId &lt;群号&gt;
    /// </summary>
    internal static class Alerter
    {
        private static readonly object Gate = new();
        private static DateTime _lastSentUtc = DateTime.MinValue;

        public static bool Enabled { get; set; }
        public static string ScriptPath { get; set; } = "";
        public static long GroupId { get; set; }
        public static List<long> GroupIds { get; set; } = new();
        public static int MinIntervalSeconds { get; set; } = 60;

        /// <summary>最近一次发送结果（给界面显示用）。</summary>
        public static string LastResult { get; private set; } = "";

        /// <summary>按配置初始化（启动时调一次）。</summary>
        public static void Configure(ManagerConfig cfg)
        {
            try
            {
                Enabled = cfg.alertEnabled;
                MinIntervalSeconds = Math.Max(5, cfg.alertMinIntervalSeconds);
                GroupId = cfg.alertGroupId;
                GroupIds = (cfg.alertGroupIds != null && cfg.alertGroupIds.Count > 0)
                    ? cfg.alertGroupIds.Where(x => x > 0).Distinct().ToList()
                    : (GroupId > 0 ? new List<long> { GroupId } : new List<long>());

                // 只认配置项；不猜路径、不内置私人脚本位置。
                ScriptPath = string.IsNullOrWhiteSpace(cfg.alertScript) ? "" : ManagerConfig.Resolve(cfg.alertScript);
            }
            catch { }
        }

        /// <summary>脚本是否可用（界面/日志里能提示）。</summary>
        public static bool Ready
        {
            get { try { return Enabled && !string.IsNullOrWhiteSpace(ScriptPath) && File.Exists(ScriptPath); } catch { return false; } }
        }

        /// <summary>
        /// 发一条告警。带最小间隔限流；真正发送放到后台线程，绝不阻塞界面。
        /// </summary>
        public static void Send(string title, string detail)
        {
            try
            {
                if (!Ready) { LastResult = "未启用或找不到上报脚本"; return; }

                lock (Gate)
                {
                    if ((DateTime.UtcNow - _lastSentUtc).TotalSeconds < MinIntervalSeconds)
                    {
                        LastResult = "被限流（距上次告警不足 " + MinIntervalSeconds + " 秒）";
                        return;
                    }
                    _lastSentUtc = DateTime.UtcNow;
                }

                var text = string.Join("\n", new[]
                {
                    "【PGame-TSManager 告警】",
                    "时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    "主机：" + Environment.MachineName,
                    title,
                    detail
                });

                var script = ScriptPath;
                var groups = GroupIds.Count > 0 ? GroupIds.ToList() : new List<long> { GroupId };
                Task.Run(() =>
                {
                    try
                    {
                        // 消息里有换行/引号，先落盘再让脚本读，避免命令行转义踩坑
                        var tmp = Path.Combine(Path.GetTempPath(), "pgtsm_alert_" + Guid.NewGuid().ToString("N") + ".txt");
                        File.WriteAllText(tmp, text, new UTF8Encoding(false));
                        try
                        {
                            var okCount = 0;
                            var errors = new List<string>();
                            foreach (var group in groups)
                            {
                                try
                                {
                                    var args = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script +
                                               "\" -Text \"" + text.Replace("\"", "'").Replace("\r", "").Replace("\n", "  |  ") +
                                               "\" -GroupId " + group;
                                    var psi = new ProcessStartInfo("powershell.exe", args)
                                    {
                                        UseShellExecute = false,
                                        CreateNoWindow = true,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true
                                    };
                                    using var p = Process.Start(psi);
                                    if (p != null)
                                    {
                                        p.WaitForExit(40000);
                                        if (p.ExitCode == 0) okCount++;
                                        else errors.Add(group + ":exit " + p.ExitCode);
                                    }
                                }
                                catch (Exception ex) { errors.Add(group + ":" + ex.Message); }
                            }
                            LastResult = errors.Count == 0
                                ? "已发送到 " + okCount + " 个群"
                                : "部分发送失败：" + string.Join("；", errors);
                        }
                        finally { try { File.Delete(tmp); } catch { } }
                    }
                    catch (Exception ex) { LastResult = "发送失败：" + ex.Message; }
                });
            }
            catch { }
        }
    }
}
