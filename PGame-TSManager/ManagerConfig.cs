using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PGameTSManager
{
    /// <summary>
    /// 单个受管服务器的描述。
    /// 指向一份已经存在的 TShock 服务端目录，只负责启动/停止/转发控制台，
    /// 不会生成、覆盖或改写该目录下的任何配置。
    /// </summary>
    public class ServerProfile
    {
        /// <summary>显示名称，同时作为界面标签。</summary>
        public string name = string.Empty;

        /// <summary>已存在的服务端目录（绝对或相对本程序目录的路径）。</summary>
        public string rootPath = string.Empty;

        /// <summary>可执行文件名，默认 TShock.Server.exe。</summary>
        public string executable = "TShock.Server.exe";

        /// <summary>启动参数，原样交给子进程，例如：-config server.properties -port 2023 -lang 7。</summary>
        public string arguments = string.Empty;

        /// <summary>是否启用该服务器。</summary>
        public bool enabled = true;

        /// <summary>备注，仅用于说明，不参与逻辑。</summary>
        public string remark = string.Empty;

        [JsonIgnore]
        public bool IsProfileMode => !string.IsNullOrWhiteSpace(rootPath);
    }

    public class ManagerConfig
    {
        // —— 旧版“在 Servers 下自建目录”模式，保留以兼容历史配置 ——
        public string worldDir = "Worlds";
        public string pluginDir = "Plugins";
        public string serverDir = "Servers";
        public string configFile = "config.json";
        public string serverExecutable = "TShock.Server.exe";
        public string serverPropertiesFile = "server.properties";
        public bool useTShockLaunchArguments = true;

        // —— 新版多服务器映射模式 ——
        /// <summary>启动前是否备份 server.properties / tshock\config.json / tshock\sscconfig.json。</summary>
        public bool backupBeforeStart = true;
        /// <summary>备份根目录，相对本程序目录。</summary>
        public string backupDir = "Backups";
        /// <summary>每台服务器保留的备份份数。</summary>
        public int backupKeep = 10;
        /// <summary>受管服务器列表。</summary>
        public List<ServerProfile> serverProfiles = new();

        private static ManagerConfig? _instance;
        public static ManagerConfig Instance => _instance ??= LoadConfig() ?? new ManagerConfig();

        private const string ConfigFileName = "config.json";

        private static ManagerConfig? LoadConfig()
        {
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    return JsonConvert.DeserializeObject<ManagerConfig>(File.ReadAllText(ConfigFileName));
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
                File.WriteAllText(ConfigFileName, JsonConvert.SerializeObject(this, Formatting.Indented));
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
                return serverProfiles.Where(p => p != null && p.enabled);
            }

            if (Directory.Exists(serverDir))
            {
                return Directory.GetDirectories(serverDir)
                    .Select(dir => new ServerProfile { name = Path.GetFileName(dir) });
            }

            return Enumerable.Empty<ServerProfile>();
        }

        public void MakeDirectories()
        {
            // 映射模式不创建、不触碰任何目录。
            if (serverProfiles != null && serverProfiles.Count > 0)
                return;

            try
            {
                if (!Directory.Exists(worldDir))
                    Directory.CreateDirectory(worldDir);
                if (!Directory.Exists(pluginDir))
                    Directory.CreateDirectory(pluginDir);
                if (!Directory.Exists(serverDir))
                    Directory.CreateDirectory(serverDir);
            }
            catch
            {
                // 目录创建失败不阻断启动。
            }
        }
    }
}