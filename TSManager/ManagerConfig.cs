using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TSManager
{
    public class ManagerConfig
    {
        public string worldDir = "Worlds";
        public string pluginDir = "Plugins";
        public string serverDir = "Servers";
        public string configFile = "config.json";

        private static ManagerConfig? _instance;
        public static ManagerConfig Instance => _instance ??= LoadConfig() ?? new ManagerConfig();

        private const string ConfigFileName = "config.json";
        private static ManagerConfig? LoadConfig()
        {
            if (File.Exists(ConfigFileName))
            {
                return JsonConvert.DeserializeObject<ManagerConfig>(File.ReadAllText(ConfigFileName));
            }

            var res = new ManagerConfig();
            File.WriteAllText(ConfigFileName, JsonConvert.SerializeObject(res, Formatting.Indented));
            return res;
        }

        public void MakeDirectories()
        {
            if (!Directory.Exists(worldDir))
                Directory.CreateDirectory(worldDir);
            if (!Directory.Exists(pluginDir))
                Directory.CreateDirectory(pluginDir);
            if (!Directory.Exists(serverDir))
                Directory.CreateDirectory(serverDir);
        }
    }
}
