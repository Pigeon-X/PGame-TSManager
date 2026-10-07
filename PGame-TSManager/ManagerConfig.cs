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
        public string worldDir = "Servers\\Worlds";
        /// <summary>插件总库（共享），相对程序目录。</summary>
        public string pluginDir = "Plugins";
        /// <summary>各服运行沙箱目录（自动生成），相对程序目录。</summary>
        public string runtimeDir = "Core\\_runtime";
        public string serverDir = "Servers\\Profiles";
        public string configFile = "config.json";
        public string serverExecutable = "TShock.Server.exe";

        // —— 运行选项 ——
        public bool backupBeforeStart = true;
        public string backupDir = "Core\\Backups";
        public string logDir = "Core\\Logs";
        public string dataDir = "Core\\Data";
        public string toolsDir = "Tools";
        public int backupKeep = 10;
        public List<ServerProfile> serverProfiles = new();

        /// <summary>运行时母本目录；留空 = 程序目录（exe/bin/i18n/runtimes/x64/GeoIP.dat 所在处）。</summary>
        public string sharedRuntimeDir = "Core";
        /// <summary>启动前按各服 config.json 从插件总库同步插件。</summary>
        public bool syncPluginsOnStart = true;
        /// <summary>未列出的插件移入 ServerPlugins.disabled（不删除）。</summary>
        public bool prunePlugins = true;
        /// <summary>被停用插件的子目录名。</summary>
        public string disabledPluginDir = "ServerPlugins.disabled";
        /// <summary>true = 每台服务器另开独立可见控制台窗口；false（默认）= 在管理器内运行。</summary>
        public bool showServerWindow = false;

        // —— 空服内存压缩：不关端口，只压缩工作集，玩家首次进入不受影响 ——
        /// <summary>是否在服务器无人时自动压缩工作集。</summary>
        public bool idleMemoryTrimEnabled = true;
        /// <summary>连续无人多少分钟后执行第一次压缩。</summary>
        public int idleMemoryTrimMinutes = 15;
        /// <summary>两次压缩之间的最小间隔分钟数。</summary>
        public int idleMemoryTrimCooldownMinutes = 30;
        /// <summary>工作集低于该值时不再压缩，避免无意义操作。</summary>
        public int idleMemoryTrimMinWorkingSetMB = 256;

        /// <summary>
        /// 「全部启动」是否按顺序逐台启动：上一台真正就绪（端口在监听）后再起下一台。
        /// 三台同时读世界会互相抢 CPU / 磁盘，表现就是界面卡很久才起来。
        /// </summary>
        public bool startAllSequential = true;

        // —— 看门狗：服务器异常退出后自动拉起 ——
        /// <summary>是否开启看门狗（异常退出自动重启）。</summary>
        public bool watchdogEnabled = true;
        /// <summary>连续自动重启次数上限，超过就放弃并告警（防止无限崩溃循环）。</summary>
        public int watchdogMaxRestarts = 3;
        /// <summary>自动重启前等待秒数（给端口/文件句柄一点释放时间）。</summary>
        public int watchdogRestartDelaySeconds = 8;
        /// <summary>运行超过这个秒数算「稳定」，重启计数清零（偶尔崩一次不会累积到上限）。</summary>
        public int watchdogStableSeconds = 300;
        /// <summary>端口/REST 连续健康检查失败多少次后判定为死服并重启。</summary>
        public int watchdogHealthFailureThreshold = 3;
        /// <summary>启动后的看门狗保护期，避免世界加载期间误判。</summary>
        public int watchdogStartupGraceSeconds = 30;
        /// <summary>端口持续异常多少秒后才允许判定死服，避免热重载或换图时短暂断流误判。</summary>
        public int watchdogUnhealthySeconds = 120;
        /// <summary>执行 /hr、/reload、/world 等维护命令后，暂停健康检查的秒数。</summary>
        public int watchdogMaintenanceSuppressSeconds = 180;
        /// <summary>是否把连续致命日志异常作为死服重启依据。</summary>
        public bool watchdogLogErrorRestartEnabled = true;
        /// <summary>窗口期内出现多少条致命日志后触发重启。</summary>
        public int watchdogLogErrorThreshold = 3;
        /// <summary>致命日志统计窗口秒数。</summary>
        public int watchdogLogErrorWindowSeconds = 60;

        // —— 告警：异常/掉线自动发测试群 ——
        /// <summary>是否自动上报测试群。</summary>
        public bool alertEnabled = true;
        /// <summary>上报脚本路径；留空 = 自动找桌面 AI维护文件\12-机器人\机器人上报测试群.ps1。</summary>
        public string alertScript = "";
        /// <summary>兼容旧配置：单个上报群号。</summary>
        public long alertGroupId = 1125570228;
        /// <summary>上报到的群号列表；未配置时回退到 alertGroupId。</summary>
        public List<long> alertGroupIds = new() { 1125570228, 561150136 };
        /// <summary>同类告警最小间隔秒数（防刷屏）。</summary>
        public int alertMinIntervalSeconds = 60;
        /// <summary>顺序启动时，单台服务器等待「就绪」的最长秒数（超时就跳过，继续下一台）。</summary>
        public int startReadyTimeoutSeconds = 240;

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
            List<ServerProfile> list;
            if (serverProfiles != null && serverProfiles.Count > 0)
            {
                list = serverProfiles.Where(p => p != null && p.enabled).ToList();
            }
            else
            {
                var legacyDir = Resolve(serverDir);
                list = Directory.Exists(legacyDir)
                    ? Directory.GetDirectories(legacyDir).Select(d => new ServerProfile { name = Path.GetFileName(d) }).ToList()
                    : new List<ServerProfile>();
            }

            // ★ 自动发现：1.PigeonServers 下存在、但没写进 serverProfiles 的服务器目录（例如手动新增的 4.xxx）。
            //   只要目录里有 TSM 清单 config.json 且写了端口，就当一台服务器接管 —— 新增只要建目录，不用改管理器配置。
            try
            {
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (serverProfiles != null)
                {
                    foreach (var p in serverProfiles.Where(x => x != null))
                    {
                        var f = SafeFullPath(p!.ResolvedRootPath);
                        if (f != null) known.Add(f);
                    }
                }

                var dir = Resolve(serverDir);
                if (Directory.Exists(dir))
                {
                    foreach (var d in Directory.GetDirectories(dir))
                    {
                        var manifestPath = Path.Combine(d, ServerProfile.ManifestFileName);
                        if (!File.Exists(manifestPath)) continue;

                        ServerManifest? mf = null;
                        try { mf = JsonConvert.DeserializeObject<ServerManifest>(File.ReadAllText(manifestPath)); } catch { }
                        if (mf == null || mf.Port == 0) continue;      // 没有端口 = 不是一台服务器

                        var full = SafeFullPath(d);
                        if (full == null || known.Contains(full)) continue;

                        list.Add(new ServerProfile
                        {
                            name = string.IsNullOrWhiteSpace(mf.Name) ? StripOrdinal(Path.GetFileName(d)) : mf.Name!,
                            rootPath = d,
                            enabled = true,
                            remark = "自动发现"
                        });
                    }
                }
            }
            catch { }

            // ★ 按「服务器目录名前面的序号」排序：1.流光城 → 2.泰拉大陆 → 3.流光神域
            //   新增服务器只要把目录叫 4.xxx / 5.xxx，就会自动排在后面，不用改配置里的顺序。
            return list
                .OrderBy(OrdinalKey)
                .ThenBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>排序用：优先取目录名开头的数字；没有就取显示名开头的数字；都没有排到最后。</summary>
        private static int OrdinalKey(ServerProfile p)
        {
            try
            {
                var folder = OrdinalOf(Path.GetFileName(p.ResolvedRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (folder > 0) return folder;
            }
            catch { }

            var byName = OrdinalOf(p.name);
            return byName > 0 ? byName : int.MaxValue;
        }

        private static string? SafeFullPath(string? path)
        {
            try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path); }
            catch { return null; }
        }

        /// <summary>"4.生存服" → "生存服"（去掉开头序号与分隔符）。</summary>
        public static string StripOrdinal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var s = text.Trim();
            var i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0) return s;
            var rest = s.Substring(i).TrimStart('.', '、', '-', '_', ' ');
            return rest.Length > 0 ? rest : s;
        }
        /// <summary>取名字开头的序号（"1.流光城" → 1，"10.xxx" → 10）。没有序号返回 0。</summary>
        public static int OrdinalOf(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var s = text.Trim();
            var i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0) return 0;
            return int.TryParse(s.Substring(0, i), out var n) ? n : 0;
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
            foreach (var d in new[] { worldDir, pluginDir, runtimeDir, backupDir, logDir, dataDir, toolsDir, serverDir })
            {
                try { var full = Resolve(d); if (!Directory.Exists(full)) Directory.CreateDirectory(full); }
                catch { }
            }
        }
    }
}
