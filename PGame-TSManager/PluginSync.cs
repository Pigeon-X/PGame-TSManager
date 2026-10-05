using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PGameTSManager
{
    /// <summary>
    /// 总插件库 -> 每服 ServerPlugins 同步。
    ///
    /// 规则：
    ///  - 按每服 config.json 的「插件」清单，从总插件库复制/更新到该服 ServerPlugins。
    ///  - 未列出的第三方插件移入 ServerPlugins.disabled（移动，不删除，可恢复）。
    ///  - TShock 自身文件（TShockAPI.*）永远保留，不参与同步。
    ///  - 清单里列了但总插件库没有的插件，保留本服现有文件并给出提示。
    /// </summary>
    internal static class PluginSync
    {
        /// <summary>
        /// 共享模式：把插件总库整体镜像到 &lt;根目录&gt;\ServerPlugins（三服共用一套插件）。
        /// 只复制/更新总库里的文件；总库里没有的（例如 TShockAPI.*）保持不动。
        /// </summary>
        public static string MirrorShared(string rootDirectory, string libraryDirectory, Action<string>? log)
        {
            var spDir = Path.Combine(rootDirectory, "ServerPlugins");
            if (!Directory.Exists(libraryDirectory))
            {
                log?.Invoke($"[插件同步] 总库不存在：{libraryDirectory}\n");
                return "missing-library";
            }
            Directory.CreateDirectory(spDir);
            int added = 0, updated = 0;
            foreach (var src in Directory.GetFiles(libraryDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(src);
                var dst = Path.Combine(spDir, name);
                if (!File.Exists(dst)) { File.Copy(src, dst, true); added++; }
                else if (!SameFile(src, dst)) { File.Copy(src, dst, true); updated++; }
            }
            var summary = $"新增 {added} / 更新 {updated}（共享 ServerPlugins）";
            log?.Invoke($"[插件同步] {summary}\n");
            return summary;
        }

        public static string Apply(
            string serverDirectory,
            string libraryDirectory,
            IReadOnlyCollection<string> enabledPlugins,
            bool prune,
            string disabledDirName,
            Action<string>? log)
        {
            var spDir = Path.Combine(serverDirectory, "ServerPlugins");
            var disabledDir = Path.Combine(serverDirectory,
                string.IsNullOrWhiteSpace(disabledDirName) ? "ServerPlugins.disabled" : disabledDirName);

            if (enabledPlugins == null || enabledPlugins.Count == 0)
            {
                log?.Invoke("[插件同步] 未配置插件清单，跳过。\n");
                return "skip";
            }

            if (!Directory.Exists(libraryDirectory))
            {
                log?.Invoke($"[插件同步] 总插件库不存在：{libraryDirectory}\n");
                return "missing-library";
            }

            Directory.CreateDirectory(spDir);

            var enabled = new HashSet<string>(
                enabledPlugins.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var libraryFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(libraryDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                libraryFiles[Path.GetFileName(f)] = f;
            }

            var added = 0;
            var updated = 0;
            var moved = 0;
            var missing = new List<string>();

            // 1) 从总插件库补齐 / 更新已启用插件
            foreach (var name in enabled)
            {
                if (!libraryFiles.TryGetValue(name, out var src))
                {
                    missing.Add(name);
                    continue;
                }

                var dest = Path.Combine(spDir, name);
                if (!File.Exists(dest))
                {
                    File.Copy(src, dest, true);
                    added++;
                }
                else if (!SameFile(src, dest))
                {
                    File.Copy(src, dest, true);
                    updated++;
                }

                CopyCompanion(libraryFiles, spDir, name, ".pdb");
            }

            if (missing.Count > 0)
            {
                log?.Invoke($"[插件同步] 总插件库缺少（保留本服现有文件）：{string.Join(", ", missing)}\n");
            }

            // 2) 未列出的插件移入禁用目录
            if (prune)
            {
                foreach (var file in Directory.GetFiles(spDir).ToList())
                {
                    var n = Path.GetFileName(file);
                    if (IsTShockOwn(n)) continue;
                    if (IsEnabled(n, enabled)) continue;

                    Directory.CreateDirectory(disabledDir);
                    var target = Path.Combine(disabledDir, n);
                    if (File.Exists(target))
                    {
                        try { File.Delete(target); } catch { }
                    }
                    File.Move(file, target, true);
                    moved++;
                    log?.Invoke($"[插件同步] 停用（移出 ServerPlugins）：{n}\n");
                }
            }

            var summary = $"新增 {added} / 更新 {updated} / 停用 {moved}，共 {enabled.Count} 个启用插件";
            log?.Invoke($"[插件同步] {summary}（库：{libraryDirectory}）\n");
            return summary;
        }

        /// <summary>某个文件是否允许留在 ServerPlugins：清单直接列出，或它是已启用 DLL 的 .pdb。</summary>
        private static bool IsEnabled(string fileName, HashSet<string> enabled)
        {
            if (enabled.Contains(fileName)) return true;
            if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                var dll = Path.GetFileNameWithoutExtension(fileName) + ".dll";
                return enabled.Contains(dll);
            }
            return false;
        }

        /// <summary>TShock 自身文件，永不参与同步/停用。</summary>
        private static bool IsTShockOwn(string fileName) =>
            fileName.StartsWith("TShockAPI", StringComparison.OrdinalIgnoreCase);

        private static void CopyCompanion(Dictionary<string, string> libraryFiles, string spDir, string name, string extension)
        {
            var baseName = Path.GetFileNameWithoutExtension(name);
            var companion = baseName + extension;
            if (!libraryFiles.TryGetValue(companion, out var src)) return;
            if (!string.Equals(extension, Path.GetExtension(name), StringComparison.OrdinalIgnoreCase))
            {
                var dest = Path.Combine(spDir, companion);
                if (!File.Exists(dest) || !SameFile(src, dest))
                {
                    File.Copy(src, dest, true);
                }
            }
        }

        private static bool SameFile(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                return Hash(fa.FullName) == Hash(fb.FullName);
            }
            catch
            {
                return false;
            }
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
    }
}