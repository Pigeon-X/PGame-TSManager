using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    /// <summary>
    /// 新建服务器向导：目录自动取下一个序号（1./2./3. → 4.），tshock 配置从模板服复制，
    /// 并把 TSM 清单 config.json 写好。管理器启动/刷新时会自动发现新目录。
    /// </summary>
    public partial class NewServerWindow : Window
    {
        private readonly ManagerConfig _cfg;
        private readonly string _serversDir;
        private readonly List<string> _templates = new();

        /// <summary>创建成功后的目录名（例如 "4.生存服"）。</summary>
        public string CreatedDirName { get; private set; } = "";

        public NewServerWindow(ManagerConfig cfg)
        {
            InitializeComponent();

            // 外框跟随主窗口：深色标题栏 + 紫色描边
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
            _cfg = cfg;
            _serversDir = ManagerConfig.Resolve(cfg.serverDir);

            var dirs = Directory.Exists(_serversDir)
                ? Directory.GetDirectories(_serversDir).Select(Path.GetFileName).Where(x => x != null).Select(x => x!).OrderBy(x => x).ToList()
                : new List<string>();
            _templates.AddRange(dirs);
            TemplateBox.ItemsSource = _templates;
            if (_templates.Count > 0) TemplateBox.SelectedIndex = 0;

            // 默认端口：游戏端口和 REST 端口各自往下排（+2），省得自己算
            ReadUsedPorts(dirs, out var games, out var rests);
            PortBox.Text = (games.Count > 0 ? games.Max() + 2 : 2025).ToString();
            RestBox.Text = (rests.Count > 0 ? rests.Max() + 2 : 7881).ToString();
        }

        private void ReadUsedPorts(List<string> dirs, out List<int> games, out List<int> rests)
        {
            games = new List<int>();
            rests = new List<int>();
            foreach (var d in dirs)
            {
                try
                {
                    var f = Path.Combine(_serversDir, d, ServerProfile.ManifestFileName);
                    if (!File.Exists(f)) continue;
                    var o = JObject.Parse(File.ReadAllText(f));
                    if (o["端口"] != null) games.Add(Convert.ToInt32(o["端口"]!.ToString()));
                    if (o["REST端口"] != null) rests.Add(Convert.ToInt32(o["REST端口"]!.ToString()));
                }
                catch { }
            }
        }

        private static int NextOrdinal(string serversDir)
        {
            var max = 0;
            try
            {
                foreach (var d in Directory.GetDirectories(serversDir))
                {
                    var n = ManagerConfig.OrdinalOf(Path.GetFileName(d));
                    if (n > max) max = n;
                }
            }
            catch { }
            return max + 1;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = ManagerConfig.StripOrdinal(NameBox.Text ?? "").Trim();
                if (name.Length == 0) { Warn("请填服务器名。"); return; }
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Warn("服务器名里有不能用在文件名里的字符。"); return; }

                if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535) { Warn("游戏端口要填 1~65535。"); return; }
                if (!int.TryParse(RestBox.Text.Trim(), out var rest) || rest < 1 || rest > 65535) { Warn("REST 端口要填 1~65535。"); return; }
                if (port == rest) { Warn("游戏端口和 REST 端口不能相同。"); return; }

                ReadUsedPorts(_templates, out var usedGames, out var usedRests);
                var used = new List<int>();
                used.AddRange(usedGames);
                used.AddRange(usedRests);
                if (used.Contains(port)) { Warn($"游戏端口 {port} 已被别的服占用。"); return; }
                if (used.Contains(rest)) { Warn($"REST 端口 {rest} 已被别的服占用。"); return; }

                var template = TemplateBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(template)) { Warn("请选一个模板服。"); return; }

                var ordinal = NextOrdinal(_serversDir);
                var dirName = ordinal + "." + name;
                var target = Path.Combine(_serversDir, dirName);
                if (Directory.Exists(target)) { Warn("目录已存在：" + target); return; }

                Directory.CreateDirectory(target);

                // 复制模板服的 tshock 配置（端口/数据库随后还要手改，向导会提示）
                var tplTshock = Path.Combine(_serversDir, template, "tshock");
                if (Directory.Exists(tplTshock))
                    CopyDirectory(tplTshock, Path.Combine(target, "tshock"));

                // 插件清单照抄模板服
                var plugins = new List<string>();
                try
                {
                    var tplManifest = Path.Combine(_serversDir, template, ServerProfile.ManifestFileName);
                    if (File.Exists(tplManifest))
                    {
                        var o = JObject.Parse(File.ReadAllText(tplManifest));
                        if (o["插件"] is JArray arr) plugins = arr.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();
                    }
                }
                catch { }

                var world = (WorldBox.Text ?? "").Trim();
                if (world.Length == 0) world = name + ".wld";

                var manifest = new JObject
                {
                    ["服务器名称"] = name,
                    ["启用"] = true,
                    ["世界"] = world,
                    ["语言"] = 7,
                    ["端口"] = port,
                    ["REST端口"] = rest,
                    ["最大玩家"] = 252,
                    ["IP"] = "0.0.0.0",
                    ["密码"] = "",
                    ["启动参数"] = "",
                    ["插件"] = new JArray(plugins),
                    ["备注"] = $"端口 {port} / REST {rest}"
                };
                File.WriteAllText(Path.Combine(target, ServerProfile.ManifestFileName),
                    manifest.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));

                CreatedDirName = dirName;
                MessageBox.Show(this,
                    $"已创建：{dirName}\n\n目录：{target}\n序号：{ordinal}\n世界：{world}（记得把 .wld 放进 Worlds\\）\n" +
                    $"端口：{port}   REST：{rest}\n插件清单：{plugins.Count} 个（照抄 {template}）\n\n" +
                    "还要手动做两件事（向导不代改，避免误伤）：\n" +
                    $"1) 打开 {dirName}\\tshock\\config.json：\n" +
                    $"   · 「Rest的端口」改成 {rest}\n" +
                    "   · MySQL 数据库名/连接信息按这台服要用的库改\n" +
                    "2) 把世界文件放进 Worlds\\ 目录\n\n" +
                    "改完在管理器里点「刷新服务器」就能看到它。",
                    "新建服务器", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "创建失败：" + ex.Message, "新建服务器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, "新建服务器", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}