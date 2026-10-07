using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    }
}
