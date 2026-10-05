using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PGameTSManager
{
    /// <summary>
    /// 无界面自检：检查配置文件、每服 TSM 配置、总插件库与三个服务器目录是否就绪，
    /// 结果写入程序目录下的 selfcheck.txt，便于一键部署后远程验证。
    /// 用法：PGame-TSManager.exe --selfcheck
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

            var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            sb.AppendLine("管理器配置: " + cfgPath + (File.Exists(cfgPath) ? " [正常]" : " [失败：不存在]"));

            ManagerConfig cfg;
            try
            {
                cfg = ManagerConfig.Instance;
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果: 失败（配置读取错误：" + ex.Message + "）");
                return Write(sb, 1);
            }

            var profiles = new List<ServerProfile>(cfg.LoadProfiles());
            sb.AppendLine("服务器数量: " + profiles.Count);
            if (profiles.Count == 0)
            {
                ok = false;
                sb.AppendLine("  [失败] 未配置任何服务器（serverProfiles 为空）");
            }

            var library = ManagerConfig.Resolve(cfg.pluginLibrary);
            sb.AppendLine("总插件库: " + library + (Directory.Exists(library) ? " [正常]" : " [失败：不存在]"));
            if (!Directory.Exists(library)) ok = false;

            foreach (var p in profiles)
            {
                sb.AppendLine();
                sb.AppendLine("· " + p.name);

                var dir = p.IsProfileMode
                    ? p.ResolvedRootPath
                    : Path.Combine(ManagerConfig.Resolve(cfg.serverDir), p.name);
                sb.AppendLine("    服务器目录 : " + dir);
                if (Directory.Exists(dir)) sb.AppendLine("    目录状态   : [正常]");
                else { ok = false; sb.AppendLine("    目录状态   : [失败：不存在]"); }

                var exeName = string.IsNullOrWhiteSpace(p.executable) ? cfg.serverExecutable : p.executable;
                var exe = cfg.useSharedRuntime
                    ? Path.Combine(ManagerConfig.Resolve(cfg.sharedRuntimeDir), exeName)
                    : Path.Combine(dir, exeName);
                if (File.Exists(exe)) sb.AppendLine("    服务端程序 : [正常] " + exeName + (cfg.useSharedRuntime ? "（共享运行时）" : ""));
                else { ok = false; sb.AppendLine("    服务端程序 : [失败：缺失] " + exe); }

                var tshockConfig = Path.Combine(dir, "tshock", "config.json");
                sb.AppendLine("    TShock配置 : " + (File.Exists(tshockConfig) ? "[正常] tshock\\config.json" : "[警告] tshock\\config.json 不存在"));

                var manifest = p.LoadManifest();
                sb.AppendLine("    TSM配置    : " + (manifest != null ? "[正常] 服务器目录 config.json" : "[警告] 未找到 config.json"));

                var args = p.ResolveArguments(out _);
                sb.AppendLine("    启动参数   : " + (string.IsNullOrWhiteSpace(args) ? "[警告] 未配置" : args));

                var pluginList = p.ResolvePlugins(manifest);
                sb.AppendLine("    启用插件   : " + pluginList.Count + " 个");
                if (pluginList.Count > 0)
                {
                    var miss = pluginList.Where(x => !File.Exists(Path.Combine(library, x))).ToList();
                    if (miss.Count > 0)
                    {
                        ok = false;
                        sb.AppendLine("    [失败] 总插件库缺少: " + string.Join(", ", miss));
                    }
                    var spDir = cfg.useSharedRuntime ? library : Path.Combine(dir, "ServerPlugins");
                    var have = Directory.Exists(spDir)
                        ? Directory.GetFiles(spDir).Count(f => pluginList.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                        : 0;
                    sb.AppendLine("    插件就绪   : " + have + " / " + pluginList.Count + (cfg.useSharedRuntime ? "（核对插件总库）" : "（本服 ServerPlugins）"));
                }
            }

            sb.AppendLine();
            sb.AppendLine(ok ? "结果: 全部正常" : "结果: 有项目未通过");
            return Write(sb, ok ? 0 : 1);
        }

        private static int Write(StringBuilder sb, int exitCode)
        {
            sb.AppendLine("退出码: " + exitCode);
            var text = sb.ToString();
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

        /// <summary>
        /// 按每服 config.json 的插件清单，从总插件库同步 ServerPlugins（不启动服务器）。
        /// 用法：PGame-TSManager.exe --syncplugins
        /// </summary>
        public static int SyncPlugins()
        {
            var sb = new StringBuilder();
            var ok = true;
            sb.AppendLine("PGame-TSManager 插件同步");
            sb.AppendLine("时间 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            ManagerConfig cfg;
            try { cfg = ManagerConfig.Instance; }
            catch (Exception ex)
            {
                sb.AppendLine("结果: 失败（配置读取错误：" + ex.Message + "）");
                return WriteNamed(sb, "syncplugins.txt", 1);
            }

            var library = ManagerConfig.Resolve(cfg.pluginLibrary);
            sb.AppendLine("总插件库: " + library);
            if (!Directory.Exists(library)) { ok = false; sb.AppendLine("  [失败] 总插件库不存在"); }

            foreach (var p in cfg.LoadProfiles())
            {
                sb.AppendLine();
                sb.AppendLine("· " + p.name);
                try
                {
                    var dir = p.IsProfileMode ? p.ResolvedRootPath : Path.Combine(ManagerConfig.Resolve(cfg.serverDir), p.name);
                    var manifest = p.LoadManifest();
                    var pluginList = p.ResolvePlugins(manifest);
                    var prune = manifest?.PrunePlugins ?? cfg.prunePlugins;
                    sb.AppendLine("    插件清单 : " + pluginList.Count + " 个");
                    if (pluginList.Count == 0) { sb.AppendLine("    [跳过] 未配置插件清单"); continue; }

                    var lib = cfg.ResolvePluginLibrary(p, manifest);
                    if (!Directory.Exists(lib)) { ok = false; sb.AppendLine("    [失败] 总插件库不存在: " + lib); continue; }

                    PluginSync.Apply(dir, lib, pluginList, prune, cfg.disabledPluginDir,
                        msg => sb.AppendLine("    " + msg.TrimEnd()));
                }
                catch (Exception ex)
                {
                    ok = false;
                    sb.AppendLine("    [失败] " + ex.Message);
                }
            }

            sb.AppendLine();
            sb.AppendLine(ok ? "结果: 同步完成" : "结果: 有项目未通过");
            return WriteNamed(sb, "syncplugins.txt", ok ? 0 : 1);
        }

        private static int WriteNamed(StringBuilder sb, string fileName, int exitCode)
        {
            sb.AppendLine("退出码: " + exitCode);
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, fileName), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
            return exitCode;
        }
    }
}