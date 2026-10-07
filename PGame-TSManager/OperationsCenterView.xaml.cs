using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PGameTSManager
{
    public sealed class HealthRow
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public Brush StateBrush { get; set; } = Brushes.Gray;
        public string Pid { get; set; } = "";
        public string Memory { get; set; } = "";
        public string Ports { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    public partial class OperationsCenterView : UserControl
    {
        private ServerContainer? _current;
        private List<ServerContainer> _containers = new();
        private readonly DispatcherTimer _timer;
        private bool _errorsOnly;

        public OperationsCenterView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (_, _) => RefreshHealth();
        }

        public void Initialize(ServerContainer? current, IEnumerable<ServerContainer> containers)
        {
            SetContext(current, containers);
            if (!_timer.IsEnabled) _timer.Start();
        }

        public void SetContext(ServerContainer? current, IEnumerable<ServerContainer> containers)
        {
            _current = current;
            _containers = containers.ToList();
            if (_current != null && !_containers.Contains(_current)) _containers.Insert(0, _current);
            LogServerBox.ItemsSource = _containers;
            LogServerBox.SelectedItem = _current;
            RefreshHealth();
            RefreshLogs();
        }

        public void RefreshAll()
        {
            RefreshHealth();
            RefreshLogs();
        }

        private void RefreshHealth()
        {
            var rows = _containers.Select(c =>
            {
                var running = c.IsRunning;
                return new HealthRow
                {
                    Name = c.Name,
                    State = running ? "运行中" : "已停止",
                    StateBrush = running ? (Brush)FindResource("Ok") : (Brush)FindResource("TextDim"),
                    Pid = running ? c.ProcessId.ToString() : "-",
                    Memory = running ? FormatSize(c.MemoryBytes) : "-",
                    Ports = $"{c.GamePort}/{c.RestPort}",
                    Detail = $"玩家 {c.PlayerText} · 插件 {c.PluginCount} · 世界 {FormatSize(c.WorldSizeBytes)}"
                };
            }).ToList();
            HealthList.ItemsSource = rows;
        }

        private void RefreshLogs()
        {
            var server = LogServerBox.SelectedItem as ServerContainer ?? _current;
            if (server == null) { LogText.Text = ""; return; }
            var path = server.TodayLogPath;
            if (!File.Exists(path))
            {
                LogText.Text = "今日暂无日志：" + path;
                LogStatus.Text = "";
                return;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                var filter = LogFilter.Text.Trim();
                var selected = lines.Where(line =>
                    (_errorsOnly ? IsErrorLine(line) : true) &&
                    (filter.Length == 0 || line.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    .TakeLast(1000);
                LogText.Text = string.Join(Environment.NewLine, selected);
                LogStatus.Text = $"显示 {LogText.Text.Count(x => x == '\n') + 1} 行 / 共 {lines.Length} 行";
                LogText.ScrollToEnd();
            }
            catch (Exception ex) { LogText.Text = "读取失败：" + ex.Message; }
        }

        private static bool IsErrorLine(string line) =>
            line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("错误", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("警告", StringComparison.OrdinalIgnoreCase);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d / 1024d).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private void RefreshHealth_Click(object _, RoutedEventArgs e) => RefreshHealth();
        private void LogFilter_TextChanged(object _, TextChangedEventArgs e) => RefreshLogs();
        private void LogServer_SelectionChanged(object _, SelectionChangedEventArgs e) => RefreshLogs();
        private void ErrorsOnly_Click(object _, RoutedEventArgs e)
        {
            _errorsOnly = !_errorsOnly;
            RefreshLogs();
        }

        private void OpenLogs_Click(object _, RoutedEventArgs e)
        {
            var server = LogServerBox.SelectedItem as ServerContainer ?? _current;
            if (server == null) return;
            var dir = Path.GetDirectoryName(server.TodayLogPath);
            try { if (dir != null) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
        }

    }
}
