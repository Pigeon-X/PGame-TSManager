using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using PGameTSManager.Annotations;

namespace PGameTSManager
{
    public class ServerContainer : INotifyPropertyChanged
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly ManagerConfig _managerConfig;
        private readonly ServerProfile _profile;
        private readonly Paragraph _para;
        private Process? _process;
        private DispatcherTimer? _tailTimer;
        private long _tailPos;
        private string? _logPath;

        public bool IsRunning
        {
            get => _process != null;
            set
            {
                if (value)
                {
                    if (!IsRunning) { Start(); OnPropertyChanged(nameof(IsRunning)); }
                }
                else
                {
                    var process = _process;
                    if (process != null)
                    {
                        try { process.Kill(); } catch { }
                        StopTail();
                        OnPropertyChanged(nameof(IsRunning));
                    }
                }
            }
        }

        public FlowDocument Document { get; init; }
        public event Action<ServerContainer>? OnTextChanged;
        private string _title;

        public string Title
        {
            get => _title;
            private set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        public Brush Foreground { get; private set; }
        public Brush Background { get; private set; }

        public string Name => string.IsNullOrWhiteSpace(_profile.name)
            ? Path.GetFileName(ServerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : _profile.name;

        public ServerContainer(ManagerConfig config, ServerProfile profile)
        {
            _managerConfig = config;
            _profile = profile;
            _para = new Paragraph();
            Document = new FlowDocument(_para);
            Foreground = new SolidColorBrush(Colors.LightGray);
            Background = new SolidColorBrush(Colors.Black);
            _title = Name;
        }

        public ServerContainer(ManagerConfig config, string serverName)
            : this(config, new ServerProfile { name = serverName })
        {
        }

        private string ServerDirectory => _profile.IsProfileMode
            ? _profile.ResolvedRootPath
            : Path.GetFullPath(Path.Combine(ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name));

        private string RuntimeDirectory => _managerConfig.ResolveRuntimeDir(_profile);

        private void Start()
        {
            try { StartCore(); }
            catch (Exception ex)
            {
                AddText($"[PGame-TSManager] 启动失败：{ex.Message}\n");
                try { _process?.Dispose(); } catch { }
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        private void StartCore()
        {
            _para.Inlines.Clear();
            StopTail();

            var manifest = _profile.LoadManifest()
                ?? throw new FileNotFoundException($"找不到该服的 config.json：{Path.Combine(ServerDirectory, ServerProfile.ManifestFileName)}");

            var serverDirectory = ServerDirectory;
            var runtimeDirectory = RuntimeDirectory;

            if (_managerConfig.backupBeforeStart) BackupServerFiles(serverDirectory);
            EnsureRuntimeSandbox(runtimeDirectory, serverDirectory);
            WriteServerProperties(runtimeDirectory, serverDirectory, manifest);

            var pluginList = _profile.ResolvePlugins(manifest);
            if (_managerConfig.syncPluginsOnStart && pluginList.Count > 0)
            {
                try
                {
                    var library = _managerConfig.ResolvePluginLibrary(_profile, manifest);
                    PluginSync.Apply(runtimeDirectory, library, pluginList,
                        manifest.PrunePlugins ?? _managerConfig.prunePlugins,
                        _managerConfig.disabledPluginDir, AddText);
                }
                catch (Exception ex) { AddText($"[插件同步] 失败：{ex.Message}\n"); }
            }

            var executable = Path.Combine(runtimeDirectory, string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable);
            if (!File.Exists(executable)) throw new FileNotFoundException($"找不到服务端可执行文件：{executable}");

            var arguments = BuildArguments(runtimeDirectory, manifest);
            var showWindow = ManagerConfig.ShowWindowOverride ?? _managerConfig.showServerWindow;

            ProcessStartInfo info;
            if (showWindow)
            {
                // 有窗口模式：单独一个可见控制台
                info = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = runtimeDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
            }
            else
            {
                // 默认模式：只有 PGame-TSManager 窗口。
                // 用「隐藏控制台 + 输出重定向到文件」：
                //   - 保留真实控制台 → TShock 的 stdin 不会 EOF，不会自己退出
                //   - 控制台隐藏     → 看不到多余的窗口
                //   - 输出进 console.log，由管理器读取显示
                _logPath = Path.Combine(runtimeDirectory, "console.log");
                try { File.WriteAllText(_logPath, string.Empty, new UTF8Encoding(false)); } catch { }
                var cmd = "/c " + Quote(executable) + " " + arguments + " >> " + Quote(_logPath) + " 2>&1";
                info = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmd,
                    WorkingDirectory = runtimeDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.Exited += (_, _) =>
            {
                var code = _process != null && _process.HasExited ? _process.ExitCode : -999;
                AddText($"---服务器已退出（退出码 {code}）---\n");
                _process?.Dispose();
                _process = null;
                StopTail();
                OnPropertyChanged(nameof(IsRunning));
            };

            AddText($"[启动] {Name}\n");
            _process.Start();

            if (!showWindow) StartTail();
            OnPropertyChanged(nameof(IsRunning));
        }

        private string BuildArguments(string runtimeDirectory, ServerManifest manifest)
        {
            var parts = new List<string>
            {
                "-config", Quote(Path.Combine(runtimeDirectory, "server.properties")),
                "-port", manifest.Port.ToString(CultureInfo.InvariantCulture),
                "-lang", manifest.Language.ToString(CultureInfo.InvariantCulture)
            };
            if (manifest.MaxPlayers > 0) { parts.Add("-maxplayers"); parts.Add(manifest.MaxPlayers.ToString(CultureInfo.InvariantCulture)); }
            if (!string.IsNullOrWhiteSpace(manifest.Password)) { parts.Add("-pass"); parts.Add(Quote(manifest.Password!)); }
            if (!string.IsNullOrWhiteSpace(_profile.arguments)) parts.Add(_profile.arguments!);
            else if (!string.IsNullOrWhiteSpace(manifest.Parameters)) parts.Add(manifest.Parameters!);
            return string.Join(" ", parts);
        }

        private void WriteServerProperties(string runtimeDirectory, string serverDirectory, ServerManifest manifest)
        {
            var worlds = ManagerConfig.Resolve(_managerConfig.worldDir);
            var world = manifest.World ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(world) && !world.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) world += ".wld";
            var worldPath = string.IsNullOrWhiteSpace(world) ? Path.Combine(worlds, "world.wld") : Path.Combine(worlds, world);

            var text = string.Join(Environment.NewLine, new[]
            {
                "# 由 PGame-TSManager 自动生成，请勿手改",
                "config=./tshock/",
                "world=" + worldPath,
                "worldpath=" + worlds + Path.DirectorySeparatorChar,
                "worldname=" + (manifest.Name ?? Name),
                "autocreate=0",
                "port=" + manifest.Port.ToString(CultureInfo.InvariantCulture),
                "language=zh-Hans",
                "upnp=0",
                ""
            });
            File.WriteAllText(Path.Combine(runtimeDirectory, "server.properties"), text, new UTF8Encoding(false));
            Directory.CreateDirectory(worlds);
            Directory.CreateDirectory(Path.Combine(serverDirectory, "tshock"));
        }

        private void EnsureRuntimeSandbox(string runtimeDirectory, string serverDirectory)
        {
            Directory.CreateDirectory(runtimeDirectory);
            var master = ManagerConfig.Resolve(_managerConfig.sharedRuntimeDir);
            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;

            LinkFile(Path.Combine(master, exeName), Path.Combine(runtimeDirectory, exeName));
            LinkFile(Path.Combine(master, "GeoIP.dat"), Path.Combine(runtimeDirectory, "GeoIP.dat"));
            foreach (var d in new[] { "bin", "i18n", "runtimes", "x64" })
                LinkDirectory(Path.Combine(master, d), Path.Combine(runtimeDirectory, d));

            var realTshock = Path.Combine(serverDirectory, "tshock");
            Directory.CreateDirectory(realTshock);
            var linkTshock = Path.Combine(runtimeDirectory, "tshock");
            if (Directory.Exists(linkTshock))
            {
                var item = new DirectoryInfo(linkTshock);
                if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    try { if (!item.EnumerateFileSystemInfos().Any()) Directory.Delete(linkTshock, true); } catch { }
                }
            }
            if (!Directory.Exists(linkTshock)) LinkDirectory(realTshock, linkTshock);

            Directory.CreateDirectory(Path.Combine(runtimeDirectory, "ServerPlugins"));
            Directory.CreateDirectory(Path.Combine(runtimeDirectory, "Logs"));
        }

        private void LinkFile(string target, string link)
        {
            if (File.Exists(link) || !File.Exists(target)) return;
            if (RunCmd($"mklink /H \"{link}\" \"{target}\"") && File.Exists(link)) return;
            try { File.Copy(target, link, true); } catch { }
        }

        private void LinkDirectory(string target, string link)
        {
            if (Directory.Exists(link) || !Directory.Exists(target)) return;
            if (RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link)) return;
            try { File.Copy(target, link, true); } catch { }
        }

        private static bool RunCmd(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(15000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        // ---------- 日志回显（隐藏控制台模式） ----------

        private void StartTail()
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            _tailPos = 0;
            _tailTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _tailTimer.Tick += (_, _) => TailOnce();
            _tailTimer.Start();
        }

        private void StopTail()
        {
            try { _tailTimer?.Stop(); } catch { }
            _tailTimer = null;
        }

        private void TailOnce()
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)) return;
                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < _tailPos) _tailPos = 0;
                fs.Seek(_tailPos, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var text = sr.ReadToEnd();
                _tailPos = fs.Position;
                if (!string.IsNullOrEmpty(text)) AddText(text);
            }
            catch { }
        }

        // ---------- 发指令：走该服 REST（不需要控制台） ----------

        public void SendText(string msg)
        {
            if (_process == null) throw new InvalidOperationException("服务器未运行。");
            var manifest = _profile.LoadManifest();
            var port = manifest?.RestPort ?? 0;
            var token = ReadRestToken();
            if (port <= 0 || string.IsNullOrEmpty(token))
            {
                AddText("[提示] 该服未开启 REST，无法发送指令。\n");
                return;
            }
            try
            {
                var url = $"http://127.0.0.1:{port}/v2/server/rawcmd?token={Uri.EscapeDataString(token)}";
                var body = JsonConvert.SerializeObject(new { cmd = msg });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = Http.PostAsync(url, content).GetAwaiter().GetResult();
                var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                AddText($"> {msg}\n{text}\n");
            }
            catch (Exception ex)
            {
                AddText($"[指令发送失败] {ex.Message}\n");
            }
        }

        private string? ReadRestToken()
        {
            try
            {
                var cfg = Path.Combine(ServerDirectory, "tshock", "config.json");
                if (!File.Exists(cfg)) return null;
                var root = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(cfg));
                var dict = root?.Settings?["Rest外部应用令牌字典"];
                if (dict == null) return null;
                foreach (var p in ((Newtonsoft.Json.Linq.JObject)dict).Properties()) return p.Name;
            }
            catch { }
            return null;
        }

        // ---------- 备份 / 目录 ----------

        private void BackupServerFiles(string serverDirectory)
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var target = Path.Combine(ManagerConfig.Resolve(_managerConfig.backupDir), SanitizeFileName(Name), stamp);
                var copied = false;
                foreach (var file in new[]
                {
                    Path.Combine(serverDirectory, "tshock", _managerConfig.configFile),
                    Path.Combine(serverDirectory, "tshock", "sscconfig.json")
                })
                {
                    if (!File.Exists(file)) continue;
                    Directory.CreateDirectory(target);
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    copied = true;
                }
                if (copied) PruneBackups(Path.Combine(ManagerConfig.Resolve(_managerConfig.backupDir), SanitizeFileName(Name)), _managerConfig.backupKeep);
            }
            catch { }
        }

        private static void PruneBackups(string profileBackupDir, int keep)
        {
            if (keep <= 0 || !Directory.Exists(profileBackupDir)) return;
            foreach (var dir in Directory.GetDirectories(profileBackupDir).OrderByDescending(d => d).Skip(keep))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "server";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // ---------- 文本 UI ----------

        private void AddText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var dispatcher = _para.Dispatcher;
            if (dispatcher == null) return;
            try { dispatcher.BeginInvoke(new Action(() => AppendText(text))); } catch { }
        }

        private void AppendText(string text)
        {
            try
            {
                _para.Inlines.Add(new Run(text) { Background = Background, Foreground = Foreground });
                while (_para.Inlines.Count > 4096)
                {
                    if (_para.Inlines.FirstInline == null) break;
                    _para.Inlines.Remove(_para.Inlines.FirstInline);
                }
                OnTextChanged?.Invoke(this);
            }
            catch { }
        }

        public override string ToString() => Name;

        public event PropertyChangedEventHandler? PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}