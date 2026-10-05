using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace TSManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ManagerConfig _cfg;
        public ObservableCollection<ServerContainer> Containers { get; } = new();
        public ServerContainer Current => ComboBox.SelectedItem as ServerContainer ?? throw new InvalidOperationException();
        public MainWindow()
        {
            InitializeComponent();
            _cfg = ManagerConfig.Instance;
            _cfg.MakeDirectories();
        }

        private void Window_Loaded(object _, RoutedEventArgs e)
        {
            foreach (var path in Directory.GetDirectories(_cfg.serverDir))
            {
                var container = new ServerContainer(_cfg, Path.GetFileName(path));
                container.OnTextChanged += sender =>
                {
                    if (sender == Current) CliTextBox.ScrollToEnd();
                };
                Containers.Add(container);
            }
            if (Containers.Count > 0) ComboBox.SelectedIndex = 0;
        }

        private void StartButton_Click(object _, RoutedEventArgs e)
        {
            Current.IsRunning = true;
        }

        private void KillButton_Click(object _, RoutedEventArgs e)
        {
            Current.IsRunning = false;
        }

        private void ComboBox_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            CliTextBox.Document = Current.Document;
            CliTextBox.ScrollToEnd();
        }
        
        private void TextBox_PreviewKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !Current.IsRunning) return;
            Current.SendText(TextBox.Text);
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
