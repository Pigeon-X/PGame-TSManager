using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PGameTSManager
{
    /// <summary>
    /// 每台服务器的 TSM 配置：声明该服从「总插件库」读取哪些插件。
    /// 对应服务器目录下的 config.json（和以前 TSM 一样）。
    /// </summary>
    public class ServerManifest
    {
        [JsonProperty("服务器名称")] public string? Name { get; set; }

        /// <summary>启动参数（留空则用管理器 config.json 里的 arguments）。</summary>
        [JsonProperty("启动参数")] public string? Arguments { get; set; }

        /// <summary>总插件库路径（相对程序目录或本服务器目录）。留空用管理器默认。</summary>
        [JsonProperty("总插件库")] public string? PluginLibrary { get; set; }

        /// <summary>该服启用的插件文件名（相对总插件库）。空 = 不做插件同步。</summary>
        [JsonProperty("插件")] public List<string>? Plugins { get; set; }

        [JsonProperty("启用")] public bool Enabled { get; set; } = true;

        [JsonProperty("备注")] public string? Remark { get; set; }

        /// <summary>是否用总插件库覆盖本服 ServerPlugins（未列出的移入禁用目录）。</summary>
        [JsonProperty("覆盖插件目录")] public bool? PrunePlugins { get; set; }
    }

    /// <summary>
    /// 单个受管服务器的描述。
    /// 指向一份已经存在的 TShock 服务端目录，只负责启动/停止/转发控制台，
    /// 不会生成、覆盖或改写该目录下的任何服务器配置。
    /// </summary>
    public class ServerProfile
    {
        /// <summary>显示名称，同时作为界面标签。</summary>
        public string name = string.Empty;

        /// <summary>已存在的服务端目录（绝对路径，或相对本程序目录的路径）。</summary>
        public string rootPath = string.Empty;

        /// <summary>可执行文件名，默认 TShock.Server.exe。</summary>
        public string executable = "TShock.Server.exe";

        /// <summary>启动参数，原样交给子进程。留空时读取服务器目录 config.json 的「启动参数」。</summary>
        public string arguments = string.Empty;

        /// <summary>是否启用该服务器。</summary>
        public bool enabled = true;

        /// <summary>备注，仅用于说明，不参与逻辑。</summary>
        public string remark = string.Empty;

        /// <summary>该服启用的插件文件名；空 = 读取服务器目录 config.json 的「插件」。</summary>
        public List<string> plugins = new();

        /// <summary>该服的总插件库覆盖；空 = 用管理器默认。</summary>
        public string pluginLibrary = string.Empty;

        [JsonIgnore]
        public bool IsProfileMode => !string.IsNullOrWhiteSpace(rootPath);

        /// <summary>把 rootPath 解析为绝对路径（相对路径以本程序目录为基准）。</summary>
        [JsonIgnore]
        public string ResolvedRootPath => Path.GetFullPath(
            Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(ManagerConfig.BaseDir, rootPath));

        /// <summary>该服目录下的 TSM 配置文件名。</summary>
        public const string ManifestFileName = "config.json";

        /// <summary>读取服务器目录下的 TSM config.json；不存在或解析失败时返回 null。</summary>
        public ServerManifest? LoadManifest()
        {
            if (!IsProfileMode) return null;
            var path = Path.Combine(ResolvedRootPath, ManifestFileName);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<ServerManifest>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>解析该服实际生效的启动参数（profile 优先，其次 manifest）。</summary>
        public string ResolveArguments(out ServerManifest? manifest)
        {
            manifest = LoadManifest();
            if (!string.IsNullOrWhiteSpace(arguments)) return arguments;
            return manifest?.Arguments ?? string.Empty;
        }

        /// <summary>解析该服实际启用的插件清单（profile 优先，其次 manifest）。</summary>
        public List<string> ResolvePlugins(ServerManifest? manifest)
        {
            if (plugins != null && plugins.Count > 0) return plugins;
            return manifest?.Plugins ?? new List<string>();
        }
    }

    public class ManagerConfig
    {
        /// <summary>本程序所在目录，所有相对路径都以它为基准，避免受工作目录影响。</summary>
        [JsonIgnore]
        public static string BaseDir => AppContext.BaseDirectory;

        /// <summary>把相对路径解析为以程序目录为基准的绝对路径。</summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return BaseDir;
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(BaseDir, path));
        }

        // —— 旧版“在 Servers 下自建目录”模式，保留以兼容历史配置 ——
        public string worldDir = "Worlds";
        public string pluginDir = "Plugins";
        public string serverDir = "Servers";
        public string configFile = "config.json";
        public string serverExecutable = "TShock.Server.exe";
        public string serverPropertiesFile = "server.properties";
        public bool useTShockLaunchArguments = true;

        // —— 新版多服务器映射模式 ——
        public bool backupBeforeStart = true;
        public string backupDir = "Backups";
        public int backupKeep = 10;
        public List<ServerProfile> serverProfiles = new();

        // —— 总插件库（三服共用）——
        /// <summary>
        /// 一个 TShock 运行时模式（默认 true）：TShock.Server.exe 与 ServerPlugins 只放在管理器根目录，
        /// 每台服务器目录只保留自己的 tshock 配置、世界与 TSM config.json。
        /// 实测：TShock 的插件目录固定等于 exe 所在目录，所以一个 exe 对应一套插件。
        /// </summary>
        public bool useSharedRuntime = true;
        /// <summary>
        /// false（默认）= 服务器在 PGame-TSManager 里启动，输出显示在管理器控制台面板；
        /// true = 每台服务器另开一个独立可见控制台窗口。
        /// </summary>
        public bool showServerWindow = false;
        /// <summary>共享运行时目录；留空 = 程序目录。</summary>
        public string sharedRuntimeDir = "";
        /// <summary>总插件库目录，相对程序目录。</summary>
        public string pluginLibrary = "Plugins";
        /// <summary>启动前按每服 config.json 同步 ServerPlugins。</summary>
        public bool syncPluginsOnStart = true;
        /// <summary>未列出的插件移入禁用目录（而不是留在 ServerPlugins）。</summary>
        public bool prunePlugins = true;
        /// <summary>被禁用插件移入的子目录名。</summary>
        public string disabledPluginDir = "ServerPlugins.disabled";

        private static ManagerConfig? _instance;
        public static ManagerConfig Instance => _instance ??= LoadConfig() ?? new ManagerConfig();

        private static string ConfigFilePath => Path.Combine(BaseDir, "config.json");

        private static ManagerConfig? LoadConfig()
        {
            var path = ConfigFilePath;
            if (File.Exists(path))
            {
                try
                {
                    return JsonConvert.DeserializeObject<ManagerConfig>(File.ReadAllText(path));
                }
                catch
                {
                    return new ManagerConfig();
                }
            }

            var res = new ManagerConfig();
            res.Save();
            return res;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch
            {
                // 配置不可写时不阻断程序启动。
            }
        }

        /// <summary>
        /// 返回需要加载的服务器。只要显式配置了 serverProfiles 就只用它；
        /// 否则回退到旧版 Servers\&lt;名称&gt; 目录扫描。
        /// </summary>
        public IEnumerable<ServerProfile> LoadProfiles()
        {
            if (serverProfiles != null && serverProfiles.Count > 0)
            {
                return serverProfiles.Where(p => p != null && p.enabled).ToList();
            }

            var legacyDir = Resolve(serverDir);
            if (Directory.Exists(legacyDir))
            {
                return Directory.GetDirectories(legacyDir)
                    .Select(dir => new ServerProfile { name = Path.GetFileName(dir) })
                    .ToList();
            }

            return Enumerable.Empty<ServerProfile>();
        }

        /// <summary>解析总插件库绝对路径（profile 覆盖优先）。</summary>
        public string ResolvePluginLibrary(ServerProfile profile, ServerManifest? manifest)
        {
            if (profile != null && !string.IsNullOrWhiteSpace(profile.pluginLibrary))
                return Resolve(profile.pluginLibrary);
            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.PluginLibrary))
            {
                var p = manifest.PluginLibrary;
                if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
                // 先按服务器目录解析，再退回程序目录
                var byServer = Path.GetFullPath(Path.Combine(profile!.ResolvedRootPath, p));
                if (Directory.Exists(byServer)) return byServer;
                var byBase = Path.Combine(BaseDir, p);
                if (Directory.Exists(byBase)) return Path.GetFullPath(byBase);
                return byServer;
            }
            return Resolve(pluginLibrary);
        }

        public void MakeDirectories()
        {
            // 映射模式不创建、不触碰任何目录。
            if (serverProfiles != null && serverProfiles.Count > 0)
                return;

            try
            {
                foreach (var dir in new[] { worldDir, pluginDir, serverDir })
                {
                    var full = Resolve(dir);
                    if (!Directory.Exists(full))
                        Directory.CreateDirectory(full);
                }
            }
            catch
            {
                // 目录创建失败不阻断启动。
            }
        }
    }
}