using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    public sealed class PlayerRow
    {
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
        public string Ip { get; set; } = "";
        public string State { get; set; } = "";
        public override string ToString() => Name;
    }

    public partial class PlayerManagerWindow : Window
    {
        private readonly ServerContainer _server;

        public PlayerManagerWindow(ServerContainer server)
        {
            InitializeComponent();
            _server = server;
            Header.Text = "玩家与权限管理 — " + server.Name;
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
            Refresh();
        }

        private PlayerRow? Selected => PlayerList.SelectedItem as PlayerRow;

        private void Refresh_Click(object _, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            var err = _server.GetRestJson("v2/server/status?players=true", out var body);
            if (err != null)
            {
                StatusText.Text = "读取失败：" + err;
                PlayerList.ItemsSource = new List<PlayerRow>();
                return;
            }

            try
            {
                var root = JObject.Parse(body);
                var rows = new List<PlayerRow>();
                if (root["players"] is JArray players)
                {
                    foreach (var token in players.OfType<JObject>())
                    {
                        rows.Add(new PlayerRow
                        {
                            Name = token.Value<string>("nickname") ?? token.Value<string>("name") ?? "",
                            Group = token.Value<string>("group") ?? "",
                            Ip = token.Value<string>("ip") ?? "",
                            State = (token.Value<bool?>("muted") ?? false) ? "已静默" : "正常"
                        });
                    }
                }
                PlayerList.ItemsSource = rows;
                StatusText.Text = "在线玩家：" + rows.Count;
            }
            catch (Exception ex)
            {
                StatusText.Text = "解析失败：" + ex.Message;
                PlayerList.ItemsSource = new List<PlayerRow>();
            }
        }

        private void Run(string command, string title)
        {
            var player = Selected;
            if (player == null)
            {
                StatusText.Text = "请先选择玩家";
                return;
            }

            if (title != "静默" && title != "解除静默" && title != "击杀")
            {
                var confirm = new ChoiceDialog(
                    "确认" + title,
                    $"确定要对 {player.Name} 执行「{title}」吗？",
                    title,
                    "",
                    "取消",
                    primaryDanger: title == "封禁" || title == "踢出") { Owner = this };
                confirm.ShowDialog();
                if (confirm.Result != ChoiceDialogResult.Primary) return;
            }

            var err = _server.SendCommandViaRest(command.Replace("{player}", player.Name), out var output);
            StatusText.Text = err == null ? (title + "成功：" + player.Name) : (title + "失败：" + err);
            Refresh();
        }

        private void Mute_Click(object _, RoutedEventArgs e) => Run("/mute {player}", "静默");
        private void Unmute_Click(object _, RoutedEventArgs e) => Run("/unmute {player}", "解除静默");
        private void Kill_Click(object _, RoutedEventArgs e) => Run("/kill {player}", "击杀");
        private void Kick_Click(object _, RoutedEventArgs e) => Run("/kick {player}", "踢出");
        private void Ban_Click(object _, RoutedEventArgs e) => Run("/ban add {player} 由 PGame-TSManager 封禁", "封禁");
    }
}
