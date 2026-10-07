using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    public class ConfigSetting : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
        public string PathText { get; set; } = "";
        public string Kind { get; set; } = "Text";
        public string TypeName { get; set; } = "文本";
        public string ValueText { get; set; } = "";
        public string RawJson { get; set; } = "";

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

    public partial class ServerSettingsWindow : Window
    {
        private sealed class ConfigDocument
        {
            public string FilePath { get; set; } = "";
            public ObservableCollection<ConfigSetting> Items { get; } = new();
        }

        private readonly ConfigDocument _config = new();
        private readonly ConfigDocument _ssc = new();
        private readonly string _serverName;

        public bool RestartRequested { get; private set; }

        public ServerSettingsWindow(string serverName, string serverDirectory)
        {
            InitializeComponent();
            _serverName = serverName;
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
            LoadDocument(_ssc, Path.Combine(tshockDir, "sscconfig.json"), "sscconfig.json");

            ConfigList.ItemsSource = _config.Items;
            SscList.ItemsSource = _ssc.Items;
            UpdateCount(ConfigList, ConfigSearch, ConfigCount, _config);
            UpdateCount(SscList, SscSearch, SscCount, _ssc);
        }

        private static void LoadDocument(ConfigDocument doc, string filePath, string fileName)
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
                Flatten(root, new List<string>(), doc.Items);
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

        private static void Flatten(JToken token, IList<string> path, ObservableCollection<ConfigSetting> items)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    path.Add(property.Name);
                    Flatten(property.Value, path, items);
                    path.RemoveAt(path.Count - 1);
                }
                return;
            }

            var setting = new ConfigSetting
            {
                PathText = string.Join(".", path),
                Path = string.Join(".", path),
                DisplayName = path.Count > 0 ? path[^1] : "(root)"
            };

            if (token is JArray array)
            {
                setting.Kind = "Complex";
                setting.TypeName = "JSON 数组";
                setting.RawJson = array.ToString(Formatting.Indented);
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

        private static bool SetByPath(JObject root, string path, JToken value)
        {
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            JToken current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                if (current is not JObject obj) return false;
                var property = obj.Property(parts[i], StringComparison.OrdinalIgnoreCase);
                if (property == null) return false;
                if (i == parts.Length - 1)
                {
                    property.Value = value;
                    return true;
                }
                current = property.Value;
            }
            return false;
        }

        private static bool SaveDocument(ConfigDocument doc, out string message)
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

                    if (!SetByPath(root, setting.Path, value))
                        throw new InvalidOperationException($"找不到配置项：{setting.Path}");
                }

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
            if (!SaveDocument(_config, out var configError))
            {
                MessageBox.Show(this, "TShock 配置保存失败：\n" + configError, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!SaveDocument(_ssc, out var sscError))
            {
                MessageBox.Show(this, "SSC 配置保存失败：\n" + sscError, "服务器设置",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
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
    }
}
