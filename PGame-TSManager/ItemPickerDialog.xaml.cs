using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PGameTSManager
{
    public partial class ItemPickerDialog : Window
    {
        public ObservableCollection<ItemNameEntry> Items { get; } = new();
        public ItemNameEntry? SelectedItem { get; private set; }

        public ItemPickerDialog()
        {
            InitializeComponent();
            ItemList.ItemsSource = Items;
            Refresh("");
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
        }

        private void Refresh(string query)
        {
            Items.Clear();
            foreach (var item in ItemCatalog.Search(query))
                Items.Add(item);
            if (Items.Count > 0) ItemList.SelectedIndex = 0;
        }

        private void SearchBox_TextChanged(object _, TextChangedEventArgs e) =>
            Refresh(SearchBox.Text);

        private void ItemList_MouseDoubleClick(object _, MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is ItemNameEntry item)
            {
                SelectedItem = item;
                DialogResult = true;
                Close();
            }
        }

        private void Choose_Click(object _, RoutedEventArgs e)
        {
            if (ItemList.SelectedItem is not ItemNameEntry item) return;
            SelectedItem = item;
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
