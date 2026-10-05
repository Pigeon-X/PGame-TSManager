using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameLauncher
{
	public class ServerConfig
    {
        public string world = string.Empty;
        public CultureName lang = CultureName.English;
        public int maxPlayer = 8;
        public ushort port = 7777;
        public string ip = "0.0.0.0";
        public string password = string.Empty;
        public string[] parameters = Array.Empty<string>();
        public string[] plugins = Array.Empty<string>();
        public enum CultureName
        {
            English = 1,
            German,
            Italian,
            French,
            Spanish,
            Russian,
            Chinese,
            Portuguese,
            Polish
        }

        public string[] CreateArgs(string worldDir)
        {
            var result = new List<string>();
            result.Add("-ip");
            result.Add(ip);
            result.Add("-port");
            result.Add(port.ToString());
            result.Add("-lang");
            result.Add(((int)lang).ToString());
            result.Add("-maxplayer");
            result.Add(maxPlayer.ToString());
            if (!string.IsNullOrEmpty(world))
            {
                result.Add("-world");
                result.Add($"{Path.Combine(worldDir, world)}.wld");
            }
            if (!string.IsNullOrEmpty(password))
            {
                result.Add("-pass");
                result.Add(password);
            }
            result.AddRange(parameters);
            return result.ToArray();
        }
    }
}
