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
using Newtonsoft.Json;
using PGameTSManager.Annotations;

namespace PGameTSManager
{
    /// <summary>
    /// 一台服务器 = PGame-TSManager 根目录这个 TShock 的一个「配置目录 + 世界」。
    ///
    /// 启动方式（等价于旧版 TSM 的思路，并已适配 TShock 6.2）：
    ///   TShock.Server.exe -config "1.PigeonServers\&lt;服&gt;\tshock" -world "Worlds\&lt;世界&gt;.wld" -port N -lang L
    ///   工作目录 = PGame-TSManager 根目录  ←→ 三服共用同一份 ServerPlugins / bin / i18n
    ///
    /// 不生成运行沙箱、不弹任何窗口：进程用 CreateNoWindow 启动，输出直接回显到管理器面板。
    /// </summary>
    public class ServerContainer : INotifyPropertyChanged
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly ManagerConfig _managerConfig;
        private readonly ServerProfile _profile;
        private readonly Paragraph _para;
        private Process? _process;

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
                        try { process.Kill(true); } catch { try { process.Kill(); } catch { } }
                        OnPropertyChanged(nameof(IsRunning));
                    }
                }
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));
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

        private static readonly SolidColorBrush ClrNormal = new(Color.FromRgb(0xE6, 0xE6, 0xEE));
        private static readonly SolidColorBrush ClrDim    = new(Color.FromRgb(0x9A, 0xA0, 0xB4));
        private static readonly SolidColorBrush ClrGood   = new(Color.FromRgb(0x22, 0xC5, 0x5E));
        private static readonly SolidColorBrush ClrWarn   = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush ClrError  = new(Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly SolidColorBrush ClrAccent = new(Color.FromRgb(0x4C, 0x8D, 0xF6));
        private static readonly SolidColorBrush ClrPlugin = new(Color.FromRgb(0x8B, 0x5C, 0xF6));

        public Brush StatusBrush => IsRunning ? ClrGood : ClrDim;
        public string StatusText => IsRunning ? "运行中" : "已停止";

        public Brush Foreground { get; private set; }
        public Brush Background { get; private set; }

        public string Name => string.IsNullOrWhiteSpace(_profile.name)
            ? Path.GetFileName(ServerDirectory.TrimEnd(Path.DirectorySeparatorChar))
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

        /// <summary>服务器配置目录：1.PigeonServers\&lt;服&gt;（config.json + tshock\）。</summary>
        private string ServerDirectory => _profile.IsProfileMode
            ? _profile.ResolvedRootPath
            : Path.GetFullPath(Path.Combine(ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name));

        private string RootDirectory => ManagerConfig.BaseDir;

        private void Start()
        {
            try { StartCore(); }
            catch (Exception ex)
            {
                AddText($"[PGame-TSManager] 启动失败：{ex.Message}\n");
                try { _process?.Dispose(); } catch { }
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void StartCore()
        {
            _para.Inlines.Clear();

            var manifest = _profile.LoadManifest()
                ?? throw new FileNotFoundException($"找不到该服的 config.json：{Path.Combine(ServerDirectory, ServerProfile.ManifestFileName)}");

            var serverDirectory = ServerDirectory;
            var root = RootDirectory;
            var tshockDir = Path.Combine(serverDirectory, "tshock");
            Directory.CreateDirectory(tshockDir);

            if (_managerConfig.backupBeforeStart) BackupServerFiles(serverDirectory);

            // 三服共用一个插件目录（= 根目录 ServerPlugins），从插件总库 Plugins 同步
            if (_managerConfig.syncPluginsOnStart)
            {
                try
                {
                    PluginSync.MirrorShared(root, ManagerConfig.Resolve(_managerConfig.pluginDir), AddText);
                }
                catch (Exception ex) { AddText($"[插件同步] 失败：{ex.Message}\n"); }
            }

            var executable = Path.Combine(root, string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable);
            if (!File.Exists(executable)) throw new FileNotFoundException($"找不到服务端可执行文件：{executable}");

            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = BuildArguments(tshockDir, manifest, root),
                WorkingDirectory = root,            // ★ 共享根目录：ServerPlugins / bin / i18n 都在这里
                CreateNoWindow = true,              // ★ 绝不弹 TShock 窗口
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            };

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, a) => { if (a.Data != null) AddText(a.Data + "\n"); };
            _process.ErrorDataReceived += (_, a) => { if (a.Data != null) AddText(a.Data + "\n"); };
            _process.Exited += (_, _) =>
            {
                var code = _process != null && _process.HasExited ? _process.ExitCode : -999;
                AddText($"---服务器已退出（退出码 {code}）---\n");
                _process?.Dispose();
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));
            };

            AddText($"[启动] {Name}  (共享 ServerPlugins，无窗口)\n");
            _process.Start();
            try { _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); } catch { }
            try { _process.StandardInput.AutoFlush = true; } catch { }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>-config &lt;该服 tshock 目录&gt; -world &lt;共享世界文件&gt; -port .. -lang ..</summary>
        private string BuildArguments(string tshockDir, ServerManifest manifest, string root)
        {
            var parts = new List<string>
            {
                "-config", Quote(tshockDir),
                "-port",   manifest.Port.ToString(CultureInfo.InvariantCulture),
                "-lang",   manifest.Language.ToString(CultureInfo.InvariantCulture)
            };

            var world = manifest.World ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(world))
            {
                if (!world.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) world += ".wld";
                var worldPath = Path.IsPathRooted(world) ? world : Path.Combine(ManagerConfig.Resolve(_managerConfig.worldDir), world);
                parts.Add("-world");
                parts.Add(Quote(worldPath));
            }

            if (manifest.MaxPlayers > 0) { parts.Add("-maxplayers"); parts.Add(manifest.MaxPlayers.ToString(CultureInfo.InvariantCulture)); }
            if (!string.IsNullOrWhiteSpace(manifest.Password)) { parts.Add("-pass"); parts.Add(Quote(manifest.Password!)); }
            if (!string.IsNullOrWhiteSpace(_profile.arguments)) parts.Add(_profile.arguments!);
            else if (!string.IsNullOrWhiteSpace(manifest.Parameters)) parts.Add(manifest.Parameters!);
            return string.Join(" ", parts);
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

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
            { try { Directory.Delete(dir, true); } catch { } }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "server";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // ---------- 发指令：走该服 REST（不需要控制台） ----------
        public void SendText(string msg)
        {
            if (_process == null) throw new InvalidOperationException("服务器未运行。");
            var manifest = _profile.LoadManifest();
            var port = manifest?.RestPort ?? 0;
            var token = ReadRestToken();
            if (port <= 0 || string.IsNullOrEmpty(token)) { AddText("[提示] 该服未开启 REST，无法发送指令。\n"); return; }
            try
            {
                var url = $"http://127.0.0.1:{port}/v2/server/rawcmd?token={Uri.EscapeDataString(token)}";
                var body = JsonConvert.SerializeObject(new { cmd = msg });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = Http.PostAsync(url, content).GetAwaiter().GetResult();
                AddText($"> {msg}\n{resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()}\n");
            }
            catch (Exception ex) { AddText($"[指令发送失败] {ex.Message}\n"); }
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

        // ---------- 彩色文本 ----------
        private static Brush ColorFor(string line)
        {
            var s = line.TrimStart();
            if (s.StartsWith(">")) return ClrAccent;
            if (s.Contains("Exception") || s.Contains("Unhandled") || s.Contains("致命") ||
                s.Contains("错误") || s.Contains("失败") || s.Contains("Error") || s.Contains("ERROR")) return ClrError;
            if (s.Contains("Warning") || s.Contains("WARN") || s.Contains("警告")) return ClrWarn;
            if (s.Contains("服务器已启动") || s.Contains("正在侦听") || s.Contains("插件同步") ||
                s.Contains("[启动]") || s.Contains("总库齐备")) return ClrGood;
            if (s.StartsWith("[")) return ClrPlugin;
            return ClrNormal;
        }

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
                foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (raw.Length == 0) continue;
                    _para.Inlines.Add(new Run(raw + "\n") { Foreground = ColorFor(raw) });
                }
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