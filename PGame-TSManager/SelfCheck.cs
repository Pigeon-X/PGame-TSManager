using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PGameTSManager
{
    /// <summary>
    /// 无界面自检：检查配置文件与三个服务器目录/可执行文件是否就绪，
    /// 结果写入程序目录下的 selfcheck.txt，便于一键部署后远程验证。
    /// 用法：PGame-TSManager.exe --selfcheck
    /// </summary>
    internal static class SelfCheck
    {
        public static int Run()
        {
            var sb = new StringBuilder();
            var ok = true;

            sb.AppendLine("PGame-TSManager selfcheck");
            sb.AppendLine("time : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("base : " + AppContext.BaseDirectory);

            var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            sb.AppendLine("config: " + cfgPath + (File.Exists(cfgPath) ? " [OK]" : " [FAIL missing]"));

            ManagerConfig cfg;
            try
            {
                cfg = ManagerConfig.Instance;
            }
            catch (Exception ex)
            {
                sb.AppendLine("RESULT: FAIL (config load error: " + ex.Message + ")");
                return Write(sb, 1);
            }

            var profiles = new List<ServerProfile>(cfg.LoadProfiles());
            sb.AppendLine("profiles: " + profiles.Count);
            if (profiles.Count == 0)
            {
                ok = false;
                sb.AppendLine("  [FAIL] 未配置任何服务器 (serverProfiles 为空)");
            }

            foreach (var p in profiles)
            {
                sb.AppendLine();
                sb.AppendLine("- " + p.name);
                var dir = p.IsProfileMode
                    ? p.ResolvedRootPath
                    : Path.Combine(ManagerConfig.Resolve(cfg.serverDir), p.name);
                sb.AppendLine("    rootPath : " + dir);
                if (Directory.Exists(dir))
                {
                    sb.AppendLine("    dir      : [OK]");
                }
                else
                {
                    ok = false;
                    sb.AppendLine("    dir      : [FAIL missing]");
                }

                var exeName = string.IsNullOrWhiteSpace(p.executable) ? cfg.serverExecutable : p.executable;
                var exe = Path.Combine(dir, exeName);
                if (File.Exists(exe))
                {
                    sb.AppendLine("    exe      : [OK] " + exeName);
                }
                else
                {
                    ok = false;
                    sb.AppendLine("    exe      : [FAIL missing] " + exeName);
                }

                var tshockConfig = Path.Combine(dir, "tshock", "config.json");
                sb.AppendLine("    tshock   : " + (File.Exists(tshockConfig) ? "[OK] tshock\\config.json" : "[WARN] tshock\\config.json 不存在"));

                sb.AppendLine("    args     : " + p.arguments);
            }

            sb.AppendLine();
            sb.AppendLine(ok ? "RESULT: OK" : "RESULT: FAIL");
            return Write(sb, ok ? 0 : 1);
        }

        private static int Write(StringBuilder sb, int exitCode)
        {
            var text = sb.ToString();
            sb.AppendLine("exit  : " + exitCode);
            text = sb.ToString();
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selfcheck.txt"), text, new UTF8Encoding(false));
            }
            catch
            {
                // 写入失败时仍返回退出码。
            }
            return exitCode;
        }
    }
}