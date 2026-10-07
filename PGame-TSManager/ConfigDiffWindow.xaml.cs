using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PGameTSManager
{
    public sealed class DiffLine
    {
        public string Kind { get; init; } = "Same";
        public string Marker => Kind == "Add" ? "+" : Kind == "Remove" ? "-" : " ";
        public string Text { get; init; } = "";
    }

    public sealed class DiffCategory
    {
        public string Name { get; init; } = "";
        public List<DiffLine> Lines { get; init; } = new();
    }

    public partial class ConfigDiffWindow : Window
    {
        private readonly List<(TabItem Tab, ListBox List, DiffCategory Category)> _tabs = new();

        public ConfigDiffWindow(IReadOnlyList<DiffCategory> categories, bool saveFlow = true)
        {
            InitializeComponent();
            ConfirmButton.Content = saveFlow ? "确认保存" : "关闭";
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };

            foreach (var category in categories)
            {
                var list = new ListBox
                {
                    ItemsSource = category.Lines,
                    ItemTemplate = (DataTemplate)FindResource("DiffLineTemplate"),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 4, 6, 4)
                };
                Tabs.Items.Add(new TabItem
                {
                    Header = category.Name + " (" + category.Lines.Count + ")",
                    Content = list
                });
                _tabs.Add(((TabItem)Tabs.Items[Tabs.Items.Count - 1], list, category));
            }
            if (Tabs.Items.Count > 0) Tabs.SelectedIndex = 0;
        }

        private void Confirm_Click(object _, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object _, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DiffSearch_TextChanged(object _, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var query = DiffSearch.Text.Trim();
            foreach (var item in _tabs)
            {
                var lines = string.IsNullOrWhiteSpace(query)
                    ? item.Category.Lines
                    : item.Category.Lines.Where(x => x.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                item.List.ItemsSource = lines;
                item.Tab.Header = item.Category.Name + " (" + lines.Count + ")";
            }
        }

        private void Export_Click(object _, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "配置差异-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"
            };
            if (dialog.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            foreach (var item in _tabs)
            {
                sb.AppendLine("[" + item.Category.Name + "]");
                foreach (var line in item.List.ItemsSource.Cast<DiffLine>())
                    sb.AppendLine(line.Marker + " " + line.Text);
                sb.AppendLine();
            }
            try { File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "配置差异",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
