using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
                StartAll();
            }
        }

        private void StartAll()
        {
            foreach (var container in Containers)
            {
                if (container.IsRunning) continue;
                try { container.IsRunning = true; }
                catch (Exception ex) { AppendLine($"[启动失败] {container.Name}：{ex.Message}"); }
            }
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

        private void StartAllButton_Click(object _, RoutedEventArgs e) => StartAll();

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

        private void TextBox_PreviewKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var current = Current;
            if (current == null || !current.IsRunning) return;
            current.SendText(TextBox.Text);
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