using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PGameTSManager
{
    public sealed class BackupEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Detail { get; set; } = "";
        public string SizeText { get; set; } = "";
    }

    public partial class BackupCenterWindow : Window
    {
        private readonly string _serverName;
        private readonly string _configPath;
        private readonly string _backupRoot;
        private readonly string _serverBackupRoot;

        public bool Restored { get; private set; }

        public BackupCenterWindow(string serverName, string configPath, string backupRoot)
        {
            InitializeComponent();
            _serverName = serverName;
            _configPath = configPath;
            _backupRoot = backupRoot;
            _serverBackupRoot = Path.Combine(backupRoot, Sanitize(serverName));
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
            Refresh();
        }

        private void Refresh()
        {
            ServerBackupList.ItemsSource = LoadServerBackups();
            ManagerBackupList.ItemsSource = LoadManagerBackups();
            UpdateButtons();
        }

        private List<BackupEntry> LoadServerBackups()
        {
            var result = new List<BackupEntry>();
            if (!Directory.Exists(_serverBackupRoot)) return result;
            foreach (var dir in Directory.GetDirectories(_serverBackupRoot).OrderByDescending(x => x))
            {
                var files = Directory.GetFiles(dir);
                if (!files.Any(f => Path.GetFileName(f).Equals("config.json", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(f).Equals("sscconfig.json", StringComparison.OrdinalIgnoreCase)))
                    continue;
                result.Add(CreateEntry(dir, "配置文件备份"));
            }
            return result;
        }

        private List<BackupEntry> LoadManagerBackups()
        {
            var result = new List<BackupEntry>();
            if (!Directory.Exists(_backupRoot)) return result;
            foreach (var dir in Directory.GetDirectories(_backupRoot).OrderByDescending(x => x))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith("manager-update-", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("tshock-update-", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(CreateEntry(dir, "管理器/核心更新备份"));
            }
            return result;
        }

        private static BackupEntry CreateEntry(string dir, string type)
        {
            var files = Directory.GetFiles(dir);
            long size = 0;
            foreach (var file in files)
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return new BackupEntry
            {
                Name = Path.GetFileName(dir),
                Path = dir,
                Detail = type + " · " + files.Length + " 个文件",
                SizeText = FormatSize(size)
            };
        }

        private static string FormatSize(long size)
        {
            if (size >= 1024 * 1024) return (size / 1024d / 1024d).ToString("0.0") + " MB";
            if (size >= 1024) return (size / 1024d).ToString("0.0") + " KB";
            return size + " B";
        }

        private BackupEntry? SelectedServer => ServerBackupList.SelectedItem as BackupEntry;
        private BackupEntry? SelectedManager => ManagerBackupList.SelectedItem as BackupEntry;
        private BackupEntry? Selected => Tabs.SelectedIndex == 0 ? SelectedServer : SelectedManager;

        private void Tabs_SelectionChanged(object _, SelectionChangedEventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            if (RestoreButton == null || Tabs == null) return;
            RestoreButton.IsEnabled = Tabs.SelectedIndex == 0 && SelectedServer != null;
        }

        private void Refresh_Click(object _, RoutedEventArgs e) => Refresh();

        private void OpenFolder_Click(object _, RoutedEventArgs e)
        {
            var path = Selected?.Path;
            if (string.IsNullOrWhiteSpace(path)) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch { }
        }

        private void Restore_Click(object _, RoutedEventArgs e)
        {
            var entry = SelectedServer;
            if (entry == null) return;
            var confirm = new ChoiceDialog(
                "确认恢复配置",
                $"将把备份 {entry.Name} 中的 config.json / sscconfig.json 恢复给 {_serverName}。\n恢复后会请求重启当前服，确定继续吗？",
                "恢复并重启",
                "",
                "取消",
                primaryDanger: true) { Owner = this };
            confirm.ShowDialog();
            if (confirm.Result != ChoiceDialogResult.Primary) return;

            try
            {
                var serverDir = Path.GetDirectoryName(Path.GetDirectoryName(_configPath)) ?? "";
                var tshockDir = Path.Combine(serverDir, "tshock");
                Directory.CreateDirectory(tshockDir);
                var safety = Path.Combine(_serverBackupRoot, "rollback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(safety);
                foreach (var name in new[] { "config.json", "sscconfig.json" })
                {
                    var current = Path.Combine(tshockDir, name);
                    if (File.Exists(current)) File.Copy(current, Path.Combine(safety, name), true);
                    var source = Path.Combine(entry.Path, name);
                    if (File.Exists(source)) File.Copy(source, current, true);
                }
                Restored = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "恢复失败：\n" + ex.Message, "回滚中心",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object _, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
