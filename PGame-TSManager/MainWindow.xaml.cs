using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PGameTSManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>命令行 --startall 时，窗口加载后自动启动全部服务器。</summary>
        public static bool StartAllOnLoad { get; set; }

        private readonly ManagerConfig _cfg;
        public ObservableCollection<ServerContainer> Containers { get; } = new();
        public ServerContainer? Current => ComboBox.SelectedItem as ServerContainer;

        public MainWindow()
        {
            InitializeComponent();
            _cfg = ManagerConfig.Instance;
            _cfg.MakeDirectories();
        }

        private void Window_Loaded(object _, RoutedEventArgs e)
        {
            foreach (var profile in _cfg.LoadProfiles())
            {
                var container = new ServerContainer(_cfg, profile);
                container.OnTextChanged += sender =>
                {
                    if (sender == Current) CliTextBox.ScrollToEnd();
                };
                Containers.Add(container);
            }

            if (Containers.Count > 0)
            {
                ComboBox.SelectedIndex = 0;
            }
            else
            {
                CliTextBox.Document.Blocks.Clear();
                CliTextBox.Document.Blocks.Add(new Paragraph(new Run(
                    "未配置任何服务器。\n请在 PGame-TSManager.config.json 的 serverProfiles 中添加服务器目录。")));
                return;
            }

            if (StartAllOnLoad)
            {
                _ = StartAllSequentialAsync();      // ★ 顺序启动（不再三台一起抢资源）
            }
        }

        private bool _startingAll;

        private void StartAll()
        {
            foreach (var container in Containers)
            {
                if (container.IsRunning) continue;
                try { container.IsRunning = true; }
                catch (Exception ex) { AppendLine($"[启动失败] {container.Name}：{ex.Message}"); }
            }
        }

        /// <summary>
        /// ★ 顺序启动：第 1 台真正就绪（游戏端口在监听、插件初始化完）之后，再起第 2 台，依次往下。
        /// 之所以要这样：三台 TShock 同时加载世界会互相抢 CPU / 磁盘，表现为“点了全部启动卡很久”。
        /// 单台超时（默认 240 秒，配置项 startReadyTimeoutSeconds）就跳过，不会卡死在某一台。
        /// </summary>
        private async Task StartAllSequentialAsync()
        {
            if (_startingAll) return;
            _startingAll = true;
            try
            {
                if (!_cfg.startAllSequential)
                {
                    AppendLine("[全部启动] 配置为同时启动（startAllSequential=false）");
                    StartAll();
                    return;
                }

                var list = Containers.ToList();
                var timeout = TimeSpan.FromSeconds(Math.Max(30, _cfg.startReadyTimeoutSeconds));
                AppendLine($"[顺序启动] 共 {list.Count} 台，逐台启动（每台最多等 {timeout.TotalSeconds:0} 秒）");

                for (var i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    var tag = $"[顺序启动] {i + 1}/{list.Count} {c.Name}";

                    if (c.IsRunning)
                    {
                        c.Log($"{tag}：已在运行，跳过\n");
                        AppendLine($"{tag}：已在运行，跳过");
                        continue;
                    }

                    c.Log($"{tag}：正在启动…\n");
                    AppendLine($"{tag}：正在启动…");
                    var startedAt = DateTime.UtcNow;

                    try { c.IsRunning = true; }
                    catch (Exception ex)
                    {
                        var bad = $"{tag}：启动失败（{ex.Message}）";
                        c.Log(bad + "\n");
                        AppendLine(bad);
                        continue;
                    }

                    var ok = await c.WaitUntilReadyAsync(timeout);
                    var secs = (DateTime.UtcNow - startedAt).TotalSeconds;
                    if (ok)
                    {
                        c.Log($"{tag}：已就绪 ✓（{secs:0} 秒）\n");
                        AppendLine($"{tag}：已就绪 ✓（{secs:0} 秒）");
                    }
                    else
                    {
                        c.Log($"{tag}：等待就绪超时（{secs:0} 秒），继续下一台\n");
                        AppendLine($"{tag}：等待就绪超时（{secs:0} 秒），继续下一台");
                    }
                }

                AppendLine("[顺序启动] 全部完成");
            }
            catch (Exception ex)
            {
                AppendLine("[顺序启动] 异常：" + ex.Message);
            }
            finally { _startingAll = false; }
        }

        private void StartButton_Click(object _, RoutedEventArgs e)
        {
            var current = Current;
            if (current == null) return;
            current.IsRunning = true;
        }

        private void KillButton_Click(object _, RoutedEventArgs e)
        {
            var current = Current;
            if (current == null) return;
            current.IsRunning = false;
        }

        private async void StartAllButton_Click(object _, RoutedEventArgs e) => await StartAllSequentialAsync();

        private void StopAllButton_Click(object _, RoutedEventArgs e)
        {
            foreach (var container in Containers)
            {
                if (!container.IsRunning) continue;
                try { container.IsRunning = false; }
                catch (Exception ex) { AppendLine($"[停止失败] {container.Name}：{ex.Message}"); }
            }
        }

        private void AppendLine(string text)
        {
            CliTextBox.Document.Blocks.Add(new Paragraph(new Run(text)));
            CliTextBox.ScrollToEnd();
        }

        private void ComboBox_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            var current = Current;
            if (current == null) return;
            CliTextBox.Document = current.Document;
            CliTextBox.ScrollToEnd();
        }

        /// <summary>输入框里没字时显示灰字提示。</summary>
        private void TextBox_TextChanged(object _, TextChangedEventArgs e)
        {
            try
            {
                Placeholder.Visibility = string.IsNullOrEmpty(TextBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private void TextBox_PreviewKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var current = Current;
            if (current == null || !current.IsRunning) return;
            current.SendText(TextBox.Text);
            TextBox.Text = string.Empty;
        }

        private void SendCurrentButton_Click(object _, RoutedEventArgs e)
        {
            var current = Current;
            if (current == null || !current.IsRunning) return;
            var text = TextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            current.SendText(text);
            TextBox.Text = string.Empty;
        }

        private void SendAllButton_Click(object _, RoutedEventArgs e)
        {
            foreach (var container in Containers)
                if (container.IsRunning) container.SendText(TextBox.Text);
            TextBox.Text = string.Empty;
        }
    }
}