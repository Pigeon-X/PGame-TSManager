using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PGameTSManager
{
    /// <summary>
    /// 无界面指令通道（给远程运维 / 自检用）。用法：
    ///   PGame-TSManager.exe --send &lt;服务器名&gt; &lt;指令&gt;
    ///   例：PGame-TSManager.exe --send 生存 "/help"
    /// 走 TShock REST 的 rawcmd，结果同时写入 sendresult.txt。
    /// </summary>
    internal static class Cli
    {
        public static int Send(string[] args)
        {
            var sb = new StringBuilder();
            var ok = false;
            try
            {
                var list = new List<string>(args);
                var i = list.FindIndex(a => a.Equals("--send", StringComparison.OrdinalIgnoreCase));
                if (i < 0 || list.Count < i + 3)
                {
                    sb.AppendLine("用法: PGame-TSManager.exe --send <服务器名> <指令>");
                    return Write(sb, 2);
                }

                var name = list[i + 1];
                var cmd = string.Join(" ", list.Skip(i + 2)).Trim();
                if (cmd.Length > 0 && cmd[0] != '/') cmd = "/" + cmd;

                var cfg = ManagerConfig.Instance;
                var profiles = new List<ServerProfile>(cfg.LoadProfiles());
                var profile = profiles.FirstOrDefault(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    sb.AppendLine("找不到服务器：" + name);
                    sb.AppendLine("可用服务器：" + string.Join(" / ", profiles.Select(p => p.name)));
                    return Write(sb, 3);
                }

                var container = new ServerContainer(cfg, profile);
                var err = container.SendCommandViaRest(cmd, out var output);
                if (err == null)
                {
                    ok = true;
                    sb.AppendLine("服务器 : " + profile.name);
                    sb.AppendLine("指令   : " + cmd);
                    sb.AppendLine("回显   :");
                    sb.AppendLine(output);
                }
                else
                {
                    sb.AppendLine("发送失败：" + err);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("异常：" + ex.Message);
            }
            return Write(sb, ok ? 0 : 1);
        }

        private static int Write(StringBuilder sb, int code)
        {
            var text = sb.ToString();
            try { Console.WriteLine(text); } catch { }
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sendresult.txt"), text, new UTF8Encoding(false));
            }
            catch { }
            return code;
        }
    }
}
