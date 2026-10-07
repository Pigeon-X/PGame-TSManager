using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PGameTSManager
{
    public partial class ColorPickerDialog : Window
    {
        private static readonly string[] Colors =
        {
            "#FFFFFF", "#E8E8E8", "#BEBEBE", "#808080", "#3F3F3F", "#000000", "#FFD166", "#F4A261",
            "#E76F51", "#FF4D6D", "#FF7AA2", "#C77DFF", "#9D4EDD", "#7B2CBF", "#5A189A", "#3C096C",
            "#BDE0FE", "#8ECAE6", "#219EBC", "#0077B6", "#00B4D8", "#48CAE4", "#90E0EF", "#ADE8F4",
            "#D8F3DC", "#95D5B2", "#52B788", "#2D6A4F", "#F8F9FA", "#DDB892", "#B08968", "#7F5539",
            "#FEE440", "#F9C74F", "#F9844A", "#F8961E", "#90BE6D", "#43AA8B", "#577590", "#277DA1"
        };

        public string ColorHex { get; private set; } = "#FFFFFF";

        public ColorPickerDialog(string initialHex)
        {
            InitializeComponent();
            ApplyHex(string.IsNullOrWhiteSpace(initialHex) ? "#FFFFFF" : initialHex);
            BuildPalette();
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
        }

        private void BuildPalette()
        {
            foreach (var hex in Colors)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var button = new Button
                {
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                    Margin = new Thickness(3),
                    ToolTip = hex,
                    Tag = hex
                };
                button.Click += (_, _) =>
                {
                    ApplyHex((string)button.Tag);
                };
                Palette.Children.Add(button);
            }
        }

        private void ApplyHex(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                RBox.Text = color.R.ToString();
                GBox.Text = color.G.ToString();
                BBox.Text = color.B.ToString();
                RefreshPreview(color);
            }
            catch
            {
                RBox.Text = "255";
                GBox.Text = "255";
                BBox.Text = "255";
                RefreshPreview(System.Windows.Media.Colors.White);
            }
        }

        private void RefreshPreview(Color color)
        {
            Preview.Background = new SolidColorBrush(color);
            ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            PreviewText.Text = ColorHex;
        }

        private void Rgb_TextChanged(object _, TextChangedEventArgs e)
        {
            if (Preview == null) return;
            byte Parse(string text)
            {
                return byte.TryParse(text, out var value) ? value : (byte)0;
            }
            RefreshPreview(Color.FromRgb(Parse(RBox.Text), Parse(GBox.Text), Parse(BBox.Text)));
        }

        private void Ok_Click(object _, RoutedEventArgs e)
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
