using System.Windows;
using System.Windows.Media;

namespace PGameTSManager
{
    public enum ChoiceDialogResult
    {
        None,
        Primary,
        Secondary,
        Cancel
    }

    /// <summary>统一玻璃风格的选择弹窗，供关闭确认等交互复用。</summary>
    public partial class ChoiceDialog : Window
    {
        public ChoiceDialogResult Result { get; private set; } = ChoiceDialogResult.None;

        public ChoiceDialog(
            string title,
            string message,
            string primaryText,
            string secondaryText = "",
            string cancelText = "取消",
            bool primaryDanger = false,
            bool secondaryDanger = true)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            PrimaryBtn.Content = primaryText;
            PrimaryBtn.Background = (Brush)FindResource(primaryDanger ? "Err" : "Accent");
            PrimaryBtn.Foreground = (Brush)FindResource(primaryDanger ? "BtnText" : "AccentText");

            if (string.IsNullOrWhiteSpace(secondaryText))
            {
                SecondaryBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                SecondaryBtn.Content = secondaryText;
                SecondaryBtn.Background = (Brush)FindResource(secondaryDanger ? "Err" : "GlassPanelHi");
                SecondaryBtn.Foreground = (Brush)FindResource(secondaryDanger ? "BtnText" : "Text");
            }

            if (string.IsNullOrWhiteSpace(cancelText))
                CancelBtn.Visibility = Visibility.Collapsed;
            else
                CancelBtn.Content = cancelText;

            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
        }

        private void Primary_Click(object _, RoutedEventArgs e)
        {
            Result = ChoiceDialogResult.Primary;
            DialogResult = true;
            Close();
        }

        private void Secondary_Click(object _, RoutedEventArgs e)
        {
            Result = ChoiceDialogResult.Secondary;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object _, RoutedEventArgs e)
        {
            Result = ChoiceDialogResult.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
