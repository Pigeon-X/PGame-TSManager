using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    /// <summary>插件开关窗口里的一行。</summary>
    public class PluginToggle
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 插件开关：勾选该服 1.PigeonServers\&lt;服&gt;\config.json 的「插件」清单。
    /// 只改这一台服的清单，保存时先备份；真正生效需要重启该服（或点「保存并重启本服」）。
    /// </summary>
    public partial class PluginWindow : Window
    {
        private readonly string _manifestPath;
        private readonly string _poolDir;
        private readonly string _serverName;
        private readonly Func<string, string?>? _hotReloadCommand;
        private readonly HashSet<string> _originalEnabled = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ProtectedPlugins = new(StringComparer.OrdinalIgnoreCase)
        {
            "TShockAPI.dll",
            "HotReload.dll"
        };

        private static readonly Dictionary<string, string> PluginDescriptions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["TShockAPI.dll"] = "TShock 核心接口，所有插件依赖",
                ["AntiCheatingTool.dll"] = "反作弊检测与异常行为防护",
                ["CGive.dll"] = "管理员发放物品和权限工具",
                ["Chameleon.dll"] = "玩家外观/身份伪装",
                ["CommandTool.dll"] = "管理员指令扩展工具",
                ["CustomPlayer.dll"] = "自定义玩家属性与数据",
                ["Dimensions.dll"] = "跨世界维度与进度联动",
                ["FixTools.dll"] = "服务器修复与配置工具",
                ["HelpPlus.dll"] = "增强 /help 指令显示",
                ["HotReload.dll"] = "运行时热重载插件",
                ["LazyAPI.dll"] = "插件开发通用 API",
                ["MapTp.dll"] = "地图传送与坐标功能",
                ["PChrome.PVP.dll"] = "PVP 对战系统",
                ["PeaceMode.dll"] = "和平模式/防误伤",
                ["Permabuffs.dll"] = "永久 Buff 管理",
                ["PGameAPI.dll"] = "PGame 服务接口",
                ["PigeonMiniGamesAPI.dll"] = "小游戏系统 API",
                ["PigeonRPG.Economy.dll"] = "RPG 经济系统",
                ["PigeonRPG.Equipment.dll"] = "RPG 装备系统",
                ["PigeonRPG.MonsterTier.dll"] = "RPG 怪物分级",
                ["PigeonRPG.ProgressGuard.dll"] = "超进度检测与防护",
                ["PigeonRPG.ProgressLoot.dll"] = "进度掉落与奖励",
                ["PigeonRPG.ProgressSync.dll"] = "多服进度同步",
                ["PigeonRPG.Runtime.dll"] = "RPG 核心运行时",
                ["PigeonRPG.Shop.dll"] = "RPG 商店系统",
                ["PigeonRPG.Skill.dll"] = "RPG 技能系统",
                ["PlayerReward.dll"] = "玩家奖励发放",
                ["ProgressBag.dll"] = "进度礼包/奖励包",
                ["StatusTextManager.dll"] = "玩家状态文本管理",
                ["TeleportRequest.dll"] = "玩家传送请求",
                ["TileHelper.dll"] = "物块/建筑辅助工具",
                ["VeinMiner.dll"] = "连锁挖矿"
            };

        private static string Describe(string name) =>
            PluginDescriptions.TryGetValue(name, out var text)
                ? text
                : "自定义或专用插件";

        /// <summary>用户点了「保存并重启本服」。</summary>
        public bool RestartRequested { get; private set; }

        public ObservableCollection<PluginToggle> Items { get; } = new();

        public PluginWindow(string serverName, string serverDirectory, string poolDir,
            Func<string, string?>? hotReloadCommand = null)
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
            _serverName = serverName;
            _manifestPath = Path.Combine(serverDirectory, ServerProfile.ManifestFileName);
            _poolDir = poolDir;
            _hotReloadCommand = hotReloadCommand;

            Header.Text = "插件开关 — " + serverName;
            SubHeader.Text = "勾选 = 这台服加载的插件（对应 config.json 的「插件」清单）。" +
                             "TShockAPI.dll、HotReload.dll 为默认必需，不能取消。可用「热重载应用」立即加载/卸载其他插件变更。";
            List.ItemsSource = Items;
            LoadItems();
        }

        private void LoadItems()
        {
            var enabled = new List<string>();
            try
            {
                if (File.Exists(_manifestPath))
                {
                    var root = JObject.Parse(File.ReadAllText(_manifestPath));
                    if (root["插件"] is JArray arr)
                        enabled = arr.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();
                }
            }
            catch { }
            _originalEnabled.Clear();
            foreach (var name in enabled) _originalEnabled.Add(name);

            var pool = new List<string>();
            try
            {
                if (Directory.Exists(_poolDir))
                    pool = Directory.GetFiles(_poolDir, "*.dll").Select(Path.GetFileName).Where(x => x != null).Select(x => x!).ToList();
            }
            catch { }

            // 顺序：TShockAPI、HotReload 永远在最前；清单里有但总库没有的显示「总库缺失」
            var all = pool.Union(enabled, StringComparer.OrdinalIgnoreCase)
                          .OrderBy(n => string.Equals(n, "TShockAPI.dll", StringComparison.OrdinalIgnoreCase) ? 0 :
                                        string.Equals(n, "HotReload.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                          .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);

            foreach (var name in all)
            {
                var inPool = pool.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                Items.Add(new PluginToggle
                {
                    Name = name,
                    Description = Describe(name),
                    Status = ProtectedPlugins.Contains(name) ? "（必需）" : inPool ? "" : "（总库缺失）",
                    Enabled = ProtectedPlugins.Contains(name) ||
                              enabled.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase))
                });
            }

            if (Items.Count == 0)
                Items.Add(new PluginToggle
                {
                    Name = "（插件总库是空的）",
                    Description = "请先把插件 DLL 放进总库",
                    Enabled = false
                });
        }

        private bool SaveManifest()
        {
            try
            {
                var missingRequired = Items.FirstOrDefault(i => ProtectedPlugins.Contains(i.Name) && !i.Enabled);
                if (missingRequired != null)
                {
                    MessageBox.Show(this, missingRequired.Name + " 是默认必需插件，不能取消。", "插件开关",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var chosen = Items.Where(i => i.Enabled).Select(i => i.Name)
                    .Union(ProtectedPlugins, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // 必需插件固定放最前，读起来清楚
                var ordered = chosen
                    .OrderBy(n => string.Equals(n, "TShockAPI.dll", StringComparison.OrdinalIgnoreCase) ? 0 :
                                  string.Equals(n, "HotReload.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                JObject root;
                if (File.Exists(_manifestPath))
                {
                    var text = File.ReadAllText(_manifestPath);
                    try { File.WriteAllText(_manifestPath + ".bak", text, new System.Text.UTF8Encoding(false)); } catch { }
                    root = JObject.Parse(text);
                }
                else
                {
                    root = new JObject();
                }

                root["插件"] = new JArray(ordered);
                File.WriteAllText(_manifestPath, root.ToString(Newtonsoft.Json.Formatting.Indented), new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "插件开关", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveManifest()) return;
            DialogResult = true;
            Close();
        }

        private void SaveRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveManifest()) return;
            RestartRequested = true;
            DialogResult = true;
            Close();
        }

        private void HotReload_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveManifest()) return;
            if (_hotReloadCommand == null)
            {
                MessageBox.Show(this, "当前服务器未运行，无法执行热重载。", "插件开关",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var hotReloadEnabled = Items.Any(x =>
                string.Equals(x.Name, "HotReload.dll", StringComparison.OrdinalIgnoreCase) && x.Enabled);
            if (!hotReloadEnabled)
            {
                MessageBox.Show(this, "当前插件清单没有启用 HotReload.dll，不能用热重载应用。\n请勾选 HotReload.dll 后重启本服，再回来使用。",
                    "插件开关", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toLoad = Items
                .Where(x => x.Enabled && !_originalEnabled.Contains(x.Name) && !ProtectedPlugins.Contains(x.Name))
                .Select(x => Path.GetFileNameWithoutExtension(x.Name))
                .ToList();
            var toUnload = Items
                .Where(x => !x.Enabled && _originalEnabled.Contains(x.Name) && !ProtectedPlugins.Contains(x.Name))
                .Select(x => Path.GetFileNameWithoutExtension(x.Name))
                .ToList();

            if (toLoad.Count == 0 && toUnload.Count == 0)
            {
                MessageBox.Show(this, "插件状态没有变化。", "热重载", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var errors = new List<string>();
            foreach (var name in toUnload)
            {
                var err = _hotReloadCommand("/hr unload " + name);
                if (!string.IsNullOrWhiteSpace(err)) errors.Add("卸载 " + name + "：" + err);
            }
            foreach (var name in toLoad)
            {
                var err = _hotReloadCommand("/hr load " + name);
                if (!string.IsNullOrWhiteSpace(err)) errors.Add("加载 " + name + "：" + err);
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "热重载未完全成功：\n\n" + string.Join("\n", errors),
                    "热重载", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
