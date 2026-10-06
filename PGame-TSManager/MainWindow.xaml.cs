using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

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
            Alerter.Configure(_cfg);                 // ★ 告警通道（异常/掉线发测试群）
            ReloadContainers();
            AppendLine(Alerter.Ready
                ? "[告警] 已启用，异常/掉线与自动重启都会发到测试群"
                : "[告警] 未启用或找不到上报脚本（config.json 的 alertEnabled / alertScript）");

            if (Containers.Count == 0)
            {
                CliTextBox.Document.Blocks.Clear();
                CliTextBox.Document.Blocks.Add(new Paragraph(new Run(
                    "未配置任何服务器。\n请在 1.PigeonServers 下建 <序号.名字> 目录，或点左下角「新建服务器」。")));
                return;
            }

            StartStatusTimer();
            BuildTrayMenu();

            if (StartAllOnLoad)
            {
                _ = StartAllSequentialAsync();      // ★ 顺序启动（不再三台一起抢资源）
            }
        }

        // ---------- 服务器列表：加载 / 刷新 ----------
        /// <summary>重新扫描 1.PigeonServers（自动发现新目录），尽量保留已有容器与选中项。</summary>
        private void ReloadContainers()
        {
            var selected = Current?.Name;
            var existing = new Dictionary<string, ServerContainer>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Containers) existing[c.Name] = c;

            Containers.Clear();
            foreach (var profile in _cfg.LoadProfiles())
            {
                if (existing.TryGetValue(profile.name, out var kept))
                {
                    Containers.Add(kept);
                    continue;
                }

                var container = new ServerContainer(_cfg, profile);
                container.OnTextChanged += sender => { if (sender == Current) CliTextBox.ScrollToEnd(); };
                container.OnAlert += (c, title, detail) =>
                {
                    c.Log($"[告警] {title} —— {detail}\n");
                    Alerter.Send(title, detail);        // ★ 看门狗/日志异常 → 测试群
                };
                Containers.Add(container);
            }

            if (Containers.Count == 0) return;

            var idx = 0;
            if (selected != null)
            {
                for (var i = 0; i < Containers.Count; i++)
                    if (string.Equals(Containers[i].Name, selected, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            }
            ComboBox.SelectedIndex = idx;
            BuildTrayMenu();
        }

        private void RefreshButton_Click(object _, RoutedEventArgs e)
        {
            ReloadContainers();
            AppendLine($"[刷新] 当前共 {Containers.Count} 台服务器");
        }

        // ---------- 在线人数：定时轮询 REST ----------
        private DispatcherTimer? _statusTimer;
        private string _lastAlertResult = "";

        private void StartStatusTimer()
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statusTimer.Tick += async (_, _) =>
            {
                foreach (var c in Containers)
                {
                    try { await c.RefreshStatusAsync(); } catch { }
                }
                UpdateTrayTip();

                // 告警发送结果回显（异步发送完才会变）
                var r = Alerter.LastResult;
                if (!string.IsNullOrEmpty(r) && r != _lastAlertResult)
                {
                    _lastAlertResult = r;
                    AppendLine("[告警] 上报状态：" + r);
                }
            };
            _statusTimer.Start();
        }

        private void UpdateTrayTip()
        {
            var sb = new StringBuilder("PGame-TSManager");
            foreach (var c in Containers)
            {
                sb.Append('\n').Append(c.ListLabel).Append(' ');
                sb.Append(c.IsRunning ? c.StatusText : "已停止");
            }
            App.UpdateTrayTip(sb.ToString());
        }

        // ---------- 托盘右键菜单 ----------
        private void BuildTrayMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add("显示管理器", null, (_, _) => App.ShowFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            foreach (var c in Containers)
            {
                var target = c;
                var sub = new System.Windows.Forms.ToolStripMenuItem(target.ListLabel + (target.IsRunning ? "（运行中）" : "（已停止）"));
                sub.DropDownItems.Add("启动", null, (_, _) =>
                {
                    try { target.IsRunning = true; } catch (Exception ex) { AppendLine($"[启动失败] {target.Name}：{ex.Message}"); }
                });
                sub.DropDownItems.Add("停止", null, (_, _) =>
                {
                    try { target.IsRunning = false; } catch { }
                });
                sub.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
                sub.DropDownItems.Add("单独启动（等就绪）", null, async (_, _) =>
                {
                    try
                    {
                        if (!target.IsRunning) target.IsRunning = true;
                        await target.WaitUntilReadyAsync(TimeSpan.FromSeconds(Math.Max(30, _cfg.startReadyTimeoutSeconds)));
                        UpdateTrayTip();
                    }
                    catch { }
                });
                menu.Items.Add(sub);
            }

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("全部启动（顺序）", null, (_, _) => { _ = StartAllSequentialAsync(); });
            menu.Items.Add("全部停止", null, (_, _) =>
            {
                foreach (var c in Containers)
                {
                    try { if (c.IsRunning) c.IsRunning = false; } catch { }
                }
            });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => App.ExitFromTray());

            App.SetTrayMenu(menu);
            UpdateTrayTip();
        }

        // ---------- 插件开关 / 新建服务器 ----------
        private void PluginButton_Click(object _, RoutedEventArgs e)
        {
            var current = Current;
            if (current == null) return;

            var pool = ManagerConfig.Resolve(_cfg.pluginDir);
            var dlg = new PluginWindow(current.Name, current.ProfileDirectory, pool) { Owner = this };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            AppendLine($"[插件开关] {current.Name} 的插件清单已保存");
            if (dlg.RestartRequested && current.IsRunning)
            {
                try { current.IsRunning = false; } catch { }
                AppendLine($"[插件开关] 正在重启 {current.Name} …");
                _ = RestartOneAsync(current);
            }
        }

        private async Task RestartOneAsync(ServerContainer c)
        {
            try
            {
                // ★ 先等旧进程真正退出，否则 IsRunning 还是 true，Start 会被跳过 → 变成"只停不起"
                await c.WaitForStoppedAsync(TimeSpan.FromSeconds(30));
                c.IsRunning = true;
                var ok = await c.WaitUntilReadyAsync(TimeSpan.FromSeconds(Math.Max(30, _cfg.startReadyTimeoutSeconds)));
                AppendLine(ok ? $"[插件开关] {c.Name} 已重启并就绪 ✓" : $"[插件开关] {c.Name} 重启后等待就绪超时");
            }
            catch (Exception ex) { AppendLine($"[插件开关] {c.Name} 重启失败：{ex.Message}"); }
        }

        private void NewServerButton_Click(object _, RoutedEventArgs e)
        {
            var dlg = new NewServerWindow(_cfg) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            ReloadContainers();
            AppendLine($"[新建服务器] 已创建 {dlg.CreatedDirName}，列表已刷新");
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
                        Alerter.Send($"服务器启动失败：{c.Name}", ex.Message);
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
                        Alerter.Send($"服务器启动后未就绪：{c.Name}",
                            $"等待 {secs:0} 秒仍未进入监听状态（端口 {c.GamePort}），已继续启动下一台，请检查。");
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
            // 走当前服务器的控制台流：既能显示，也会写进 Logs\<服>-日期.log（AppendLine 直接加段落就绕过了落盘）
            var c = Current;
            if (c != null)
            {
                c.Log(text + "\n");
                CliTextBox.ScrollToEnd();
                return;
            }
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

        private void SendQuickCommand(string command)
        {
            var current = Current;
            if (current == null || !current.IsRunning)
            {
                AppendLine("[快捷指令] 当前服务器未运行");
                return;
            }
            try { current.SendText(command); }
            catch (Exception ex) { AppendLine("[快捷指令] 发送失败：" + ex.Message); }
        }

        private void QuickSave_Click(object _, RoutedEventArgs e) => SendQuickCommand("/save");

        private void QuickReload_Click(object _, RoutedEventArgs e) => SendQuickCommand("/ac reload");

        private void QuickBroadcast_Click(object _, RoutedEventArgs e)
        {
            var text = TextBox.Text.Trim();
            if (text.Length == 0)
            {
                AppendLine("[快捷指令] 先在输入框填写广播内容");
                TextBox.Focus();
                return;
            }
            SendQuickCommand("/bc " + text);
            TextBox.Text = string.Empty;
        }

        private void QuickOff_Click(object _, RoutedEventArgs e)
        {
            var current = Current;
            if (current == null || !current.IsRunning) { AppendLine("[快捷指令] 当前服务器未运行"); return; }
            var result = MessageBox.Show(this, "确定向「" + current.Name + "」发送 /off 并关闭服务器吗？", "关闭当前服", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) SendQuickCommand("/off");
        }
    }
}
