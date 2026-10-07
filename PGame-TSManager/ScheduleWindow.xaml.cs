using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PGameTSManager
{
    public partial class ScheduleWindow : Window
    {
        private readonly List<string> _serverNames;
        private List<ScheduledTask> _tasks = new();

        public ScheduleWindow(IEnumerable<string> serverNames)
        {
            InitializeComponent();
            _serverNames = serverNames.ToList();
            ServerBox.ItemsSource = _serverNames;
            if (_serverNames.Count > 0) ServerBox.SelectedIndex = 0;
            ActionBox.SelectedIndex = 0;
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
            Reload();
        }

        private ScheduledTask? Selected => TaskList.SelectedItem as ScheduledTask;

        private void Reload()
        {
            _tasks = ScheduledTaskStore.Load();
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = _tasks;
            StatusText.Text = "任务数：" + _tasks.Count;
        }

        private void TaskList_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            var task = Selected;
            if (task == null) return;
            ServerBox.SelectedItem = task.ServerName;
            ActionBox.SelectedIndex = task.Action switch { "broadcast" => 1, "restart" => 2, _ => 0 };
            TimeBox.Text = task.Time;
            MessageBox.Text = task.Message;
            EnabledCheck.IsChecked = task.Enabled;
        }

        private bool TryRead(out ScheduledTask task)
        {
            task = new ScheduledTask();
            var server = ServerBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(server))
            {
                StatusText.Text = "请选择服务器";
                return false;
            }
            if (!DateTime.TryParseExact(TimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                StatusText.Text = "时间格式应为 HH:mm";
                return false;
            }
            task.ServerName = server;
            task.Action = ((ComboBoxItem)ActionBox.SelectedItem).Tag?.ToString() ?? "save";
            task.Time = time.ToString("HH:mm");
            task.Message = MessageBox.Text.Trim();
            task.Enabled = EnabledCheck.IsChecked == true;
            return true;
        }

        private void Add_Click(object _, RoutedEventArgs e)
        {
            if (!TryRead(out var task)) return;
            task.Id = Guid.NewGuid().ToString("N");
            _tasks.Add(task);
            ScheduledTaskStore.Save(_tasks);
            Reload();
        }

        private void Update_Click(object _, RoutedEventArgs e)
        {
            var current = Selected;
            if (current == null || !TryRead(out var task)) return;
            current.ServerName = task.ServerName;
            current.Action = task.Action;
            current.Time = task.Time;
            current.Message = task.Message;
            current.Enabled = task.Enabled;
            ScheduledTaskStore.Save(_tasks);
            Reload();
        }

        private void Delete_Click(object _, RoutedEventArgs e)
        {
            var current = Selected;
            if (current == null) return;
            _tasks.Remove(current);
            ScheduledTaskStore.Save(_tasks);
            Reload();
        }

        private void Close_Click(object _, RoutedEventArgs e) => Close();
    }
}
