using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PGameTSManager
{
    /// <summary>
    /// 无界面自检 / 插件同步。用法：
    ///   PGame-TSManager.exe --selfcheck     检查配置、世界、插件是否齐备
    ///   PGame-TSManager.exe --syncplugins   只把总库插件同步到各服运行沙箱
    /// 结果分别写入 selfcheck.txt / syncplugins.txt。
    /// </summary>
    internal static class SelfCheck
    {
        public static int Run()
        {
            var sb = new StringBuilder();
            var ok = true;

            sb.AppendLine("PGame-TSManager 自检");
            sb.AppendLine("时间 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("目录 : " + AppContext.BaseDirectory);

            ManagerConfig cfg;
            try { cfg = ManagerConfig.Instance; }
            catch (Exception ex) { sb.AppendLine("结果: 失败（配置读取错误：" + ex.Message + "）"); return Write(sb, 1); }

            var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            sb.AppendLine("管理器配置: " + (File.Exists(cfgPath) ? "[正常]" : "[失败：不存在]"));

            var runtime = ManagerConfig.Resolve(cfg.sharedRuntimeDir);
            var exeName = string.IsNullOrWhiteSpace(cfg.serverExecutable) ? "TShock.Server.exe" : cfg.serverExecutable;
            var exe = Path.Combine(runtime, exeName);
            sb.AppendLine("运行时母本: " + runtime + (File.Exists(exe) ? " [正常 " + exeName + "]" : " [失败：缺少 " + exeName + "]"));
            if (!File.Exists(exe)) ok = false;

            var pool = ManagerConfig.Resolve(cfg.pluginDir);
            sb.AppendLine("插件总库 : " + pool + (Directory.Exists(pool) ? " [正常 " + Directory.GetFiles(pool, "*.dll").Length + " 个 dll]" : " [失败：不存在]"));
            if (!Directory.Exists(pool)) ok = false;

            var worlds = ManagerConfig.Resolve(cfg.worldDir);
            sb.AppendLine("世界目录 : " + worlds + (Directory.Exists(worlds) ? " [正常]" : " [警告：不存在]"));
            var profiles = new List<ServerProfile>(cfg.LoadProfiles());
            sb.AppendLine("服务器数量: " + profiles.Count);
            if (profiles.Count == 0) { ok = false; sb.AppendLine("  [失败] 未配置任何服务器"); }

            foreach (var p in profiles)
            {
                sb.AppendLine();
                sb.AppendLine("· " + p.name);
                var dir = p.IsProfileMode ? p.ResolvedRootPath : Path.Combine(ManagerConfig.Resolve(cfg.serverDir), p.name);
                sb.AppendLine("    配置目录 : " + dir);
                if (!Directory.Exists(dir)) { ok = false; sb.AppendLine("    目录状态 : [失败：不存在]"); continue; }

                var manifest = p.LoadManifest();
                if (manifest == null) { ok = false; sb.AppendLine("    TSM配置  : [失败] 缺 config.json"); }
                else sb.AppendLine("    TSM配置  : [正常] config.json");

                var tshockCfg = Path.Combine(dir, "tshock", "config.json");
                sb.AppendLine("    TShock   : " + (File.Exists(tshockCfg) ? "[正常] tshock\\config.json" : "[失败] 缺 tshock\\config.json"));
                if (!File.Exists(tshockCfg)) ok = false;

                if (manifest != null)
                {
                    var world = manifest.World ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(world) && !world.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) world += ".wld";
                    var worldPath = string.IsNullOrWhiteSpace(world) ? null : Path.Combine(worlds, world);
                    if (worldPath == null) sb.AppendLine("    世界     : [警告] 未配置「世界」");
                    else if (File.Exists(worldPath)) sb.AppendLine("    世界     : [正常] " + world);
                    else { ok = false; sb.AppendLine("    世界     : [失败] 缺 " + Path.Combine(worlds, world)); }

                    sb.AppendLine("    端口     : " + manifest.Port + " / REST " + manifest.RestPort + " / 语言 " + manifest.Language);
                }

                var plugins = p.ResolvePlugins(manifest);
                sb.AppendLine("    插件清单 : " + plugins.Count + " 个");
                if (plugins.Count > 0)
                {
                    var miss = plugins.Where(x => !File.Exists(Path.Combine(pool, x))).ToList();
                    if (miss.Count > 0)
                    {
                        ok = false;
                        sb.AppendLine("    [失败] 总库缺少: " + string.Join(", ", miss));
                    }
                    else sb.AppendLine("    [正常] 总库齐备");
                }
            }

            sb.AppendLine();
            sb.AppendLine(ok ? "结果: 全部正常" : "结果: 有项目未通过");
            return Write(sb, ok ? 0 : 1);
        }

        public static int SyncPlugins()
        {
            var sb = new StringBuilder();
            var ok = true;
            sb.AppendLine("PGame-TSManager 插件同步");
            sb.AppendLine("时间 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            ManagerConfig cfg;
            try { cfg = ManagerConfig.Instance; }
            catch (Exception ex) { sb.AppendLine("结果: 失败（配置读取错误：" + ex.Message + "）"); return WriteNamed(sb, "syncplugins.txt", 1); }

            var pool = ManagerConfig.Resolve(cfg.pluginDir);
            sb.AppendLine("插件总库: " + pool);
            if (!Directory.Exists(pool)) { ok = false; sb.AppendLine("  [失败] 插件总库不存在"); }

            try
            {
                PluginSync.MirrorShared(ManagerConfig.Resolve(""), pool, msg => sb.AppendLine("  " + msg.TrimEnd()));
            }
            catch (Exception ex) { ok = false; sb.AppendLine("  [失败] " + ex.Message); }

            sb.AppendLine();
            sb.AppendLine(ok ? "结果: 同步完成" : "结果: 有项目未通过");
            return WriteNamed(sb, "syncplugins.txt", ok ? 0 : 1);
        }

        private static int Write(StringBuilder sb, int exitCode) => WriteNamed(sb, "selfcheck.txt", exitCode);

        private static int WriteNamed(StringBuilder sb, string fileName, int exitCode)
        {
            sb.AppendLine("退出码: " + exitCode);
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, fileName), sb.ToString(), new UTF8Encoding(false)); }
            catch { }
            return exitCode;
        }
    }
}
