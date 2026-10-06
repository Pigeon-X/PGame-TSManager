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
        public string Note { get; set; } = "";
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

        /// <summary>用户点了「保存并重启本服」。</summary>
        public bool RestartRequested { get; private set; }

        public ObservableCollection<PluginToggle> Items { get; } = new();

        public PluginWindow(string serverName, string serverDirectory, string poolDir)
        {
            InitializeComponent();
            _serverName = serverName;
            _manifestPath = Path.Combine(serverDirectory, ServerProfile.ManifestFileName);
            _poolDir = poolDir;

            Header.Text = "插件开关 — " + serverName;
            SubHeader.Text = "勾选 = 这台服加载的插件（对应 config.json 的「插件」清单）。" +
                             "TShockAPI.dll 必选。保存后需要重启该服才会生效。";
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

            var pool = new List<string>();
            try
            {
                if (Directory.Exists(_poolDir))
                    pool = Directory.GetFiles(_poolDir, "*.dll").Select(Path.GetFileName).Where(x => x != null).Select(x => x!).ToList();
            }
            catch { }

            // 顺序：TShockAPI 永远第一，其余按名字；清单里有但总库没有的显示「总库缺失」
            var all = pool.Union(enabled, StringComparer.OrdinalIgnoreCase)
                          .OrderBy(n => string.Equals(n, "TShockAPI.dll", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                          .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);

            foreach (var name in all)
            {
                var inPool = pool.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                Items.Add(new PluginToggle
                {
                    Name = name,
                    Note = inPool ? "" : "（总库缺失）",
                    Enabled = enabled.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase))
                });
            }

            if (Items.Count == 0)
                Items.Add(new PluginToggle { Name = "（插件总库是空的）", Enabled = false });
        }

        private bool SaveManifest()
        {
            try
            {
                if (Items.Any(i => string.Equals(i.Name, "TShockAPI.dll", StringComparison.OrdinalIgnoreCase) && !i.Enabled))
                {
                    MessageBox.Show(this, "TShockAPI.dll 是必须加载的，不能取消。", "插件开关",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var chosen = Items.Where(i => i.Enabled).Select(i => i.Name).ToList();
                // TShockAPI 放最前，读起来清楚
                var ordered = chosen
                    .OrderBy(n => string.Equals(n, "TShockAPI.dll", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
    }
}