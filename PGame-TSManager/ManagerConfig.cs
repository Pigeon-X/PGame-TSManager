using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PGameTSManager
{
    /// <summary>
    /// 每台服务器的 TSM 配置（沿用旧版 TSManager 的 ServerConfig 字段）：
    /// 世界、语言、端口、人数、密码、额外参数，以及该服要加载的插件清单。
    /// 对应 1.PigeonServers\&lt;服&gt;\config.json。
    /// </summary>
    public class ServerManifest
    {
        [JsonProperty("服务器名称")] public string? Name { get; set; }
        [JsonProperty("启用")] public bool Enabled { get; set; } = true;

        /// <summary>世界文件名（相对 Worlds\，可不带 .wld）。</summary>
        [JsonProperty("世界")] public string? World { get; set; }
        [JsonProperty("语言")] public int Language { get; set; } = 7;      // 7 = 中文
        [JsonProperty("端口")] public ushort Port { get; set; }
        [JsonProperty("REST端口")] public int RestPort { get; set; }
        [JsonProperty("最大玩家")] public int MaxPlayers { get; set; } = 252;
        [JsonProperty("IP")] public string Ip { get; set; } = "0.0.0.0";
        [JsonProperty("密码")] public string Password { get; set; } = string.Empty;

        /// <summary>额外启动参数（原样追加）。</summary>
        [JsonProperty("启动参数")] public string? Parameters { get; set; }

        /// <summary>该服要加载的插件文件名（相对 Plugins\ 总库，含 TShockAPI.dll）。</summary>
        [JsonProperty("插件")] public List<string>? Plugins { get; set; }

        [JsonProperty("备注")] public string? Remark { get; set; }

        [JsonProperty("插件总库")] public string? PluginLibrary { get; set; }
        [JsonProperty("覆盖插件目录")] public bool? PrunePlugins { get; set; }
    }

    /// <summary>单个受管服务器：指向 1.PigeonServers\&lt;服&gt;（只放 config.json + tshock\）。</summary>
    public class ServerProfile
    {
        public string name = string.Empty;
        public string rootPath = string.Empty;
        public string executable = "TShock.Server.exe";
        public string arguments = string.Empty;
        public bool enabled = true;
        public string remark = string.Empty;
        public List<string> plugins = new();
        public string pluginLibrary = string.Empty;

        public const string ManifestFileName = "config.json";

        [JsonIgnore]
        public bool IsProfileMode => !string.IsNullOrWhiteSpace(rootPath);

        [JsonIgnore]
        public string ResolvedRootPath => Path.GetFullPath(
            Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(ManagerConfig.BaseDir, rootPath));

        public ServerManifest? LoadManifest()
        {
            if (!IsProfileMode) return null;
            var path = Path.Combine(ResolvedRootPath, ManifestFileName);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<ServerManifest>(File.ReadAllText(path)); }
            catch { return null; }
        }

        public string ResolveArguments(out ServerManifest? manifest)
        {
            manifest = LoadManifest();
            if (!string.IsNullOrWhiteSpace(arguments)) return arguments;
            return manifest?.Parameters ?? string.Empty;
        }

        public List<string> ResolvePlugins(ServerManifest? manifest)
        {
            if (plugins != null && plugins.Count > 0) return plugins;
            return manifest?.Plugins ?? new List<string>();
        }
    }

    public class ManagerConfig
    {
        [JsonIgnore]
        public static string BaseDir => AppContext.BaseDirectory;

        /// <summary>命令行 --nowindow 时临时覆盖 showServerWindow（仅调试用）。</summary>
        [JsonIgnore]
        public static bool? ShowWindowOverride = null;

        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return BaseDir;
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(BaseDir, path));
        }

        // —— 旧版 TSManager 目录约定 ——
        /// <summary>世界文件目录（共享），相对程序目录。</summary>
        public string worldDir = "Worlds";
        /// <summary>插件总库（共享），相对程序目录。</summary>
        public string pluginDir = "Plugins";
        /// <summary>各服运行沙箱目录（自动生成），相对程序目录。</summary>
        public string runtimeDir = "_runtime";
        public string serverDir = "1.PigeonServers";
        public string configFile = "config.json";
        public string serverExecutable = "TShock.Server.exe";

        // —— 运行选项 ——
        public bool backupBeforeStart = true;
        public string backupDir = "Backups";
        public int backupKeep = 10;
        public List<ServerProfile> serverProfiles = new();

        /// <summary>运行时母本目录；留空 = 程序目录（exe/bin/i18n/runtimes/x64/GeoIP.dat 所在处）。</summary>
        public string sharedRuntimeDir = "";
        /// <summary>启动前按各服 config.json 从插件总库同步插件。</summary>
        public bool syncPluginsOnStart = true;
        /// <summary>未列出的插件移入 ServerPlugins.disabled（不删除）。</summary>
        public bool prunePlugins = true;
        /// <summary>被停用插件的子目录名。</summary>
        public string disabledPluginDir = "ServerPlugins.disabled";
        /// <summary>true = 每台服务器另开独立可见控制台窗口；false（默认）= 在管理器内运行。</summary>
        public bool showServerWindow = false;

        private static ManagerConfig? _instance;
        public static ManagerConfig Instance => _instance ??= LoadConfig() ?? new ManagerConfig();

        private static string ConfigFilePath => Path.Combine(BaseDir, "config.json");

        private static ManagerConfig? LoadConfig()
        {
            var path = ConfigFilePath;
            if (File.Exists(path))
            {
                try { return JsonConvert.DeserializeObject<ManagerConfig>(File.ReadAllText(path)); }
                catch { return new ManagerConfig(); }
            }
            var res = new ManagerConfig();
            res.Save();
            return res;
        }

        public void Save()
        {
            try { File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(this, Formatting.Indented)); }
            catch { }
        }

        public IEnumerable<ServerProfile> LoadProfiles()
        {
            if (serverProfiles != null && serverProfiles.Count > 0)
                return serverProfiles.Where(p => p != null && p.enabled).ToList();

            var legacyDir = Resolve(serverDir);
            if (Directory.Exists(legacyDir))
                return Directory.GetDirectories(legacyDir).Select(d => new ServerProfile { name = Path.GetFileName(d) }).ToList();

            return Enumerable.Empty<ServerProfile>();
        }

        public string ResolvePluginLibrary(ServerProfile profile, ServerManifest? manifest)
        {
            if (profile != null && !string.IsNullOrWhiteSpace(profile.pluginLibrary))
                return Resolve(profile.pluginLibrary);
            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.PluginLibrary))
            {
                var p = manifest.PluginLibrary;
                if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
                var byBase = Path.Combine(BaseDir, p);
                if (Directory.Exists(byBase)) return Path.GetFullPath(byBase);
                return Path.GetFullPath(Path.Combine(profile!.ResolvedRootPath, p));
            }
            return Resolve(pluginDir);
        }

        /// <summary>该服的运行沙箱目录（exe/bin/ServerPlugins/server.properties/Logs 都在这里）。</summary>
        public string ResolveRuntimeDir(ServerProfile profile)
        {
            var safe = string.Join("_", profile.name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(Resolve(runtimeDir), safe);
        }

        public void MakeDirectories()
        {
            foreach (var d in new[] { worldDir, pluginDir, runtimeDir })
            {
                try { var full = Resolve(d); if (!Directory.Exists(full)) Directory.CreateDirectory(full); }
                catch { }
            }
        }
    }
}