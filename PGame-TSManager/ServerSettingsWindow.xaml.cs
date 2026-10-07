using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    public class ConfigSetting : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
        public string PathText { get; set; } = "";
        public List<string> Segments { get; set; } = new();
        public string Kind { get; set; } = "Text";
        public string TypeName { get; set; } = "文本";
        public string ValueText { get; set; } = "";
        public string RawJson { get; set; } = "";

        private string _colorHex = "#FFFFFF";
        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex == value) return;
                _colorHex = value;
                Raise(nameof(ColorHex));
                Raise(nameof(ColorBrush));
            }
        }

        public Brush ColorBrush
        {
            get
            {
                try { return (Brush)new BrushConverter().ConvertFromString(ColorHex)!; }
                catch { return Brushes.White; }
            }
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool _boolValue;
        public bool BoolValue
        {
            get => _boolValue;
            set
            {
                if (_boolValue == value) return;
                _boolValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolDisplay)));
            }
        }

        public string BoolDisplay => BoolValue ? "true" : "false";
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class SscInventoryItem : INotifyPropertyChanged
    {
        private int _netId;
        private string _stackText = "1";
        private int _stackValue = 1;
        private bool _favorited;

        public int NetId
        {
            get => _netId;
            set
            {
                if (_netId == value) return;
                _netId = value;
                Raise(nameof(NetId));
                Raise(nameof(DisplayName));
                Raise(nameof(Detail));
            }
        }

        public string StackText
        {
            get => _stackText;
            set
            {
                if (_stackText == value) return;
                _stackText = value;
                Raise(nameof(StackText));
                if (int.TryParse(value, out var parsed) && parsed >= 1)
                {
                    _stackValue = parsed;
                    Raise(nameof(StackValue));
                }
            }
        }

        public int StackValue
        {
            get => _stackValue;
            set
            {
                if (_stackValue == value) return;
                _stackValue = value;
                _stackText = value.ToString();
                Raise(nameof(StackValue));
                Raise(nameof(StackText));
            }
        }

        public int PrefixId { get; set; }

        public bool Favorited
        {
            get => _favorited;
            set { if (_favorited != value) { _favorited = value; Raise(nameof(Favorited)); } }
        }

        public string DisplayName => ItemCatalog.NameOf(NetId);
        public string Detail => "netID " + NetId;
        public IReadOnlyList<PrefixOption> PrefixOptions => ItemCatalog.CommonPrefixes;

        public void SetItem(ItemNameEntry item)
        {
            NetId = item.Id;
            Raise(nameof(DisplayName));
            Raise(nameof(Detail));
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ServerSettingsWindow : Window
    {
        private sealed class ConfigDocument
        {
            public string FilePath { get; set; } = "";
            public ObservableCollection<ConfigSetting> Items { get; } = new();
        }

        private readonly ConfigDocument _config = new();
        private readonly ConfigDocument _ssc = new();
        private readonly ObservableCollection<SscInventoryItem> _inventoryItems = new();
        private readonly string _serverName;
        private readonly Func<string, string?>? _reloadCommand;

        private static readonly Dictionary<string, string> SscNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Enabled"] = "启用 SSC（服务端角色）",
            ["ServerSideCharacterSave"] = "服务端角色保存间隔",
            ["LogonDiscardThreshold"] = "登录丢弃阈值",
            ["StartingHealth"] = "初始生命值",
            ["StartingMana"] = "初始法力值",
            ["WarnPlayersAboutBypassPermission"] = "绕过权限时警告玩家",
            ["KeepPlayerAppearance"] = "保留玩家外观",
            ["StartingInventory"] = "初始物品"
        };

        public bool RestartRequested { get; private set; }

        public ServerSettingsWindow(string serverName, string serverDirectory,
            Func<string, string?>? reloadCommand = null)
        {
            InitializeComponent();
            _serverName = serverName;
            _reloadCommand = reloadCommand;
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };

            Header.Text = "服务器设置 — " + serverName;
            SubHeader.Text = "修改会先备份原文件；保存后需要重启该服才会生效，复杂项按 JSON 原样编辑。";

            var tshockDir = Path.Combine(serverDirectory, "tshock");
            LoadDocument(_config, Path.Combine(tshockDir, "config.json"), "config.json");
            var sscPath = Path.Combine(tshockDir, "sscconfig.json");
            LoadDocument(_ssc, sscPath, "sscconfig.json", skipStartingInventory: true);
            LoadInventory(sscPath);

            ConfigList.ItemsSource = _config.Items;
            SscList.ItemsSource = _ssc.Items;
            InventoryList.ItemsSource = _inventoryItems;
            UpdateCount(ConfigList, ConfigSearch, ConfigCount, _config);
            UpdateCount(SscList, SscSearch, SscCount, _ssc);
        }

        private static void LoadDocument(ConfigDocument doc, string filePath, string fileName, bool skipStartingInventory = false)
        {
            doc.FilePath = filePath;
            if (!File.Exists(filePath))
            {
                doc.Items.Add(new ConfigSetting
                {
                    DisplayName = fileName,
                    Path = filePath,
                    Kind = "Info",
                    ValueText = "文件不存在"
                });
                return;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(filePath, System.Text.Encoding.UTF8));
                Flatten(root, new List<string>(), doc.Items, skipStartingInventory);
            }
            catch (Exception ex)
            {
                doc.Items.Add(new ConfigSetting
                {
                    DisplayName = fileName,
                    Path = filePath,
                    Kind = "Info",
                    ValueText = "JSON 读取失败：" + ex.Message
                });
            }
        }

        private static void Flatten(JToken token, IList<string> path, ObservableCollection<ConfigSetting> items,
            bool skipStartingInventory = false)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (skipStartingInventory && path.Count == 1 && path[0] == "Settings" &&
                        string.Equals(property.Name, "StartingInventory", StringComparison.OrdinalIgnoreCase))
                        continue;
                    path.Add(property.Name);
                    Flatten(property.Value, path, items, skipStartingInventory);
                    path.RemoveAt(path.Count - 1);
                }
                return;
            }

            var setting = new ConfigSetting
            {
                PathText = string.Join(".", path),
                Path = string.Join(".", path),
                Segments = path.ToList(),
                DisplayName = path.Count > 0 ? path[^1] : "(root)"
            };
            if (skipStartingInventory && path.Count == 2 && path[0] == "Settings" &&
                SscNames.TryGetValue(path[1], out var localizedName))
            {
                setting.DisplayName = localizedName;
                setting.PathText = "SSC · " + localizedName;
            }

            if (token is JArray array && path.Count == 2 && path[0] == "Settings" &&
                path[1].Contains("颜色(RGB)", StringComparison.OrdinalIgnoreCase) && array.Count >= 3)
            {
                var r = Math.Clamp(array[0].Value<int>(), 0, 255);
                var g = Math.Clamp(array[1].Value<int>(), 0, 255);
                var b = Math.Clamp(array[2].Value<int>(), 0, 255);
                setting.Kind = "Color";
                setting.TypeName = "颜色";
                setting.ColorHex = $"#{r:X2}{g:X2}{b:X2}";
                setting.ValueText = setting.ColorHex;
            }
            else if (token is JArray jsonArray)
            {
                setting.Kind = "Complex";
                setting.TypeName = "JSON 数组";
                setting.RawJson = jsonArray.ToString(Formatting.Indented);
                setting.ValueText = setting.RawJson;
            }
            else if (token is JObject nested)
            {
                setting.Kind = "Complex";
                setting.TypeName = "JSON 对象";
                setting.RawJson = nested.ToString(Formatting.Indented);
                setting.ValueText = setting.RawJson;
            }
            else if (token is JValue value)
            {
                switch (value.Type)
                {
                    case JTokenType.Boolean:
                        setting.Kind = "Bool";
                        setting.TypeName = "布尔";
                        setting.BoolValue = value.Value<bool>();
                        break;
                    case JTokenType.Integer:
                        setting.Kind = "Text";
                        setting.TypeName = "整数";
                        setting.ValueText = value.ToString();
                        break;
                    case JTokenType.Float:
                        setting.Kind = "Text";
                        setting.TypeName = "小数";
                        setting.ValueText = value.ToString();
                        break;
                    case JTokenType.Null:
                        setting.Kind = "Text";
                        setting.TypeName = "空值";
                        setting.ValueText = "";
                        break;
                    default:
                        setting.Kind = "Text";
                        setting.TypeName = "文本";
                        setting.ValueText = value.ToString();
                        break;
                }
            }
            items.Add(setting);
        }

        private void LoadInventory(string filePath)
        {
            _inventoryItems.Clear();
            if (!File.Exists(filePath)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(filePath, System.Text.Encoding.UTF8));
                if (root["Settings"]?["StartingInventory"] is not JArray array) return;
                foreach (var token in array.OfType<JObject>())
                {
                    var netId = token.Value<int?>("netID") ?? 0;
                    if (netId <= 0) continue;
                    _inventoryItems.Add(new SscInventoryItem
                    {
                        NetId = netId,
                        StackValue = token.Value<int?>("stack") ?? 1,
                        PrefixId = token.Value<int?>("prefix") ?? 0,
                        Favorited = token.Value<bool?>("favorited") ?? false
                    });
                }
            }
            catch { }
        }

        private static bool SetByPath(JObject root, IReadOnlyList<string> parts, JToken value)
        {
            JToken current = root;
            for (var i = 0; i < parts.Count; i++)
            {
                if (current is not JObject obj) return false;
                var property = obj.Property(parts[i], StringComparison.OrdinalIgnoreCase);
                if (property == null) return false;
                if (i == parts.Count - 1)
                {
                    property.Value = value;
                    return true;
                }
                current = property.Value;
            }
            return false;
        }

        private static bool SaveDocument(ConfigDocument doc, JArray? inventory, out string message)
        {
            message = "";
            if (!File.Exists(doc.FilePath)) return true;
            try
            {
                var root = JObject.Parse(File.ReadAllText(doc.FilePath, System.Text.Encoding.UTF8));
                foreach (var setting in doc.Items)
                {
                    if (setting.Kind == "Info") continue;
                    JToken value;
                    if (setting.Kind == "Bool")
                    {
                        value = new JValue(setting.BoolValue);
                    }
                    else if (setting.Kind == "Color")
                    {
                        var color = (Color)ColorConverter.ConvertFromString(setting.ColorHex);
                        value = new JArray(color.R, color.G, color.B);
                    }
                    else if (setting.Kind == "Complex")
                    {
                        try { value = JToken.Parse(setting.RawJson); }
                        catch (Exception ex) { throw new FormatException($"{setting.DisplayName} 的 JSON 格式不正确：{ex.Message}"); }
                    }
                    else if (setting.TypeName == "整数")
                    {
                        if (!long.TryParse(setting.ValueText, out var number))
                            throw new FormatException($"{setting.DisplayName} 需要填整数。");
                        value = new JValue(number);
                    }
                    else if (setting.TypeName == "小数")
                    {
                        if (!double.TryParse(setting.ValueText, out var number))
                            throw new FormatException($"{setting.DisplayName} 需要填数字。");
                        value = new JValue(number);
                    }
                    else if (setting.TypeName == "空值" && string.IsNullOrWhiteSpace(setting.ValueText))
                    {
                        value = JValue.CreateNull();
                    }
                    else
                    {
                        value = new JValue(setting.ValueText ?? "");
                    }

                    if (!SetByPath(root, setting.Segments, value))
                        throw new InvalidOperationException($"找不到配置项：{setting.Path}");
                }
                if (inventory != null && !SetByPath(root, new[] { "Settings", "StartingInventory" }, inventory))
                    throw new InvalidOperationException("找不到配置项：Settings.StartingInventory");

                try { File.WriteAllText(doc.FilePath + ".bak", File.ReadAllText(doc.FilePath, System.Text.Encoding.UTF8), new System.Text.UTF8Encoding(false)); } catch { }
                File.WriteAllText(doc.FilePath, root.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private bool SaveAll()
        {
            if (!TryBuildInventory(out var inventory, out var inventoryError))
            {
                MessageBox.Show(this, "初始物品保存失败：\n" + inventoryError, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!SaveDocument(_config, null, out var configError))
            {
                MessageBox.Show(this, "TShock 配置保存失败：\n" + configError, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!SaveDocument(_ssc, inventory, out var sscError))
            {
                MessageBox.Show(this, "SSC 配置保存失败：\n" + sscError, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private bool TryBuildInventory(out JArray inventory, out string error)
        {
            inventory = new JArray();
            error = "";
            try
            {
                foreach (var item in _inventoryItems)
                {
                    if (item.NetId <= 0) throw new FormatException("物品 netID 必须大于 0。");
                    if (!int.TryParse(item.StackText.Trim(), out var stack) || stack < 1)
                        throw new FormatException(ItemCatalog.NameOf(item.NetId) + " 的数量必须大于 0。");

                    inventory.Add(new JObject
                    {
                        ["netID"] = item.NetId,
                        ["prefix"] = item.PrefixId,
                        ["stack"] = stack,
                        ["favorited"] = item.Favorited
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                inventory = new JArray();
                return false;
            }
        }

        private static void UpdateCount(ItemsControl list, TextBox search, TextBlock label, ConfigDocument doc)
        {
            var query = (search.Text ?? "").Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? doc.Items.ToList()
                : doc.Items.Where(x =>
                    x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.PathText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.ValueText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = filtered;
            label.Text = filtered.Count + " / " + doc.Items.Count;
        }

        private void ConfigSearch_TextChanged(object _, TextChangedEventArgs e) =>
            UpdateCount(ConfigList, ConfigSearch, ConfigCount, _config);

        private void SscSearch_TextChanged(object _, TextChangedEventArgs e) =>
            UpdateCount(SscList, SscSearch, SscCount, _ssc);

        private void Cancel_Click(object _, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AddInventoryItem_Click(object _, RoutedEventArgs e)
        {
            var picker = new ItemPickerDialog { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedItem == null) return;
            _inventoryItems.Add(new SscInventoryItem
            {
                NetId = picker.SelectedItem.Id,
                StackText = "1",
                PrefixId = 0
            });
        }

        private void PickInventoryItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not SscInventoryItem row) return;
            var picker = new ItemPickerDialog { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedItem == null) return;
            row.SetItem(picker.SelectedItem);
        }

        private void RemoveInventoryItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is SscInventoryItem row)
                _inventoryItems.Remove(row);
        }

        private void Save_Click(object _, RoutedEventArgs e)
        {
            if (!SaveAll()) return;
            StatusText.Text = "已保存，重启服务器后生效";
            DialogResult = true;
            Close();
        }

        private void SaveRestart_Click(object _, RoutedEventArgs e)
        {
            if (!SaveAll()) return;
            RestartRequested = true;
            DialogResult = true;
            Close();
        }

        private void SaveReloadTshock_Click(object _, RoutedEventArgs e) =>
            SaveAndReload("/reload", "TShock");

        private void SaveReloadSsc_Click(object _, RoutedEventArgs e) =>
            SaveAndReload("/reload ssc", "SSC");

        private void SaveAndReload(string command, string label)
        {
            if (!SaveAll()) return;
            if (_reloadCommand == null)
            {
                MessageBox.Show(this, $"当前服务器未运行，无法执行重载 {label}。", "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var error = _reloadCommand(command);
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(this, $"保存成功，但重载 {label} 失败：\n" + error, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"已保存，重载 {label} 失败";
                return;
            }

            StatusText.Text = $"已保存并重载 {label}";
            DialogResult = true;
            Close();
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ConfigSetting setting) return;
            var picker = new ColorPickerDialog(setting.ColorHex) { Owner = this };
            if (picker.ShowDialog() != true) return;
            setting.ColorHex = picker.ColorHex;
        }
    }
}
