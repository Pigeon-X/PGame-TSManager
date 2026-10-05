using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
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
        private string? consoleTitle;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;

        /// <summary>按标题找到刚创建的控制台窗口并隐藏（后台轮询最多 15 秒）。</summary>
        private void HideConsoleWindowAsync()
        {
            var title = consoleTitle;
            if (string.IsNullOrEmpty(title)) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                for (var i = 0; i < 150; i++)
                {
                    try
                    {
                        var h = FindWindow(null, title);
                        if (h != IntPtr.Zero)
                        {
                            ShowWindow(h, SW_HIDE);
                            return;
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(100);
                }
            });
        }

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
                        StopTail();
                        OnPropertyChanged(nameof(IsRunning));
                        OnPropertyChanged(nameof(StatusBrush));
                        OnPropertyChanged(nameof(StatusText));
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

        // ---- 界面配色 ----
        private static readonly SolidColorBrush ClrNormal = new(Color.FromRgb(0xE6, 0xE6, 0xEE));
        private static readonly SolidColorBrush ClrDim    = new(Color.FromRgb(0x9A, 0xA0, 0xB4));
        private static readonly SolidColorBrush ClrGood   = new(Color.FromRgb(0x22, 0xC5, 0x5E));
        private static readonly SolidColorBrush ClrWarn   = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush ClrError  = new(Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly SolidColorBrush ClrAccent = new(Color.FromRgb(0x4C, 0x8D, 0xF6));
        private static readonly SolidColorBrush ClrPlugin = new(Color.FromRgb(0x8B, 0x5C, 0xF6));

        /// <summary>状态灯颜色：运行中=绿，已停止=灰。</summary>
        public Brush StatusBrush => IsRunning ? ClrGood : ClrDim;
        public string StatusText => IsRunning ? "运行中" : "已停止";

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
            var showWindow = false;   // 规则：绝不显示 TShock 窗口，永远隐藏控制台

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
                // 规则：只有 PGame-TSManager 一个窗口，绝不带出 TShock 控制台。
                // 直接用「隐藏窗口」启动：进程仍然拥有一个真实控制台（stdin 不会 EOF，
                // TShock 6.2 不会自己退出），但窗口是隐藏的，看不到。
                // 面板回显改为读 TShock 自己写的日志：_runtime\<服>\Logs\<日期>.log
                // 实测唯一稳定的做法（与手动命令一致）：
                //   cmd /c start "" /b /d <沙箱> <exe> <参数>
                // cmd 自己会拿到一个（隐藏的）控制台，服务器用 start /b 继承它 →
                // stdin 是真正的控制台输入，TShock 6.2 不会因 EOF 自行退出；同时看不到任何窗口。
                Directory.CreateDirectory(Path.Combine(runtimeDirectory, "Logs"));
                // ★ B 方案：让服务器拿到「真实控制台」，再把窗口藏起来。
                //   GUI（WPF）父进程默认没有控制台，子进程会拿不到 stdin → TShock 报错时
                //   Console.ForegroundColor 抛 IOException → 被主循环吞掉 → 静默 exit 0。
                //   做法：用 cmd 起（CreateNoWindow=false 时 Windows 会给 cmd 分配一个新控制台），
                //   服务器继承该控制台；再用 FindWindow 按标题把那个控制台窗口 SW_HIDE 掉。
                consoleTitle = "PGame-" + Name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                var cmdArgs = "/c title " + consoleTitle + " & cd /d " + Quote(runtimeDirectory) +
                              " & " + Quote(executable) + " " + arguments;
                info = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    WorkingDirectory = runtimeDirectory,
                    UseShellExecute = true,      // ShellExecute 会为 cmd 新建控制台
                    WindowStyle = ProcessWindowStyle.Minimized   // 先最小化，随后按标题隐藏
                };
            }

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, args) => { if (args.Data != null) AddText(args.Data + "\n"); };
            _process.ErrorDataReceived += (_, args) => { if (args.Data != null) AddText(args.Data + "\n"); };
            _process.Exited += (_, _) =>
            {
                var code = _process != null && _process.HasExited ? _process.ExitCode : -999;
                AddText($"---服务器已退出（退出码 {code}）---\n");
                _process?.Dispose();
                _process = null;
                StopTail();
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));
            };

            AddText($"[启动] {Name}\n");
            _process.Start();
            if (!showWindow) HideConsoleWindowAsync();
            if (info.RedirectStandardOutput) _process.BeginOutputReadLine();
            if (info.RedirectStandardError) _process.BeginErrorReadLine();

            StartTail(runtimeDirectory);
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>用 WMI 创建进程（唯一能保证 TShock 有真实控制台、又不弹窗的方式）。</summary>
        private void LaunchViaWmi(string commandLine, out int pid)
        {
            pid = 0;
            try
            {
                using var cls = new ManagementClass("Win32_Process");
                var inParams = cls.GetMethodParameters("Create");
                inParams["CommandLine"] = commandLine;
                var outParams = cls.InvokeMethod("Create", inParams, null);
                var rc = outParams?["ReturnValue"];
                if (outParams != null) pid = Convert.ToInt32(outParams["ProcessId"] ?? 0);
                if (pid <= 0) AddText($"[启动] WMI 创建进程失败：ReturnValue={rc}\n");
            }
            catch (Exception ex)
            {
                AddText($"[启动] WMI 调用异常：{ex.GetType().Name}: {ex.Message}\n");
            }
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

        private string? _logDir;

        private void StartTail(string runtimeDirectory)
        {
            _logDir = Path.Combine(runtimeDirectory, "Logs");
            Directory.CreateDirectory(_logDir);
            _tailPos = 0;
            _tailTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
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
                if (string.IsNullOrEmpty(_logDir) || !Directory.Exists(_logDir)) return;
                var newest = new DirectoryInfo(_logDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (newest == null) return;
                if (_logPath != newest.FullName) { _logPath = newest.FullName; _tailPos = 0; }
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

        private static Brush ColorFor(string line)
        {
            var s = line.TrimStart();
            if (s.StartsWith(">")) return ClrAccent;
            if (s.Contains("Exception") || s.Contains("Unhandled") || s.Contains("致命") ||
                s.Contains("错误") || s.Contains("失败") || s.Contains("Error") || s.Contains("ERROR")) return ClrError;
            if (s.Contains("Warning") || s.Contains("WARN") || s.Contains("警告")) return ClrWarn;
            if (s.Contains("服务器已启动") || s.Contains("正在侦听") || s.Contains("插件同步") ||
                s.Contains("[启动]") || s.Contains("总库齐备")) return ClrGood;
            if (s.StartsWith("[Server API]")) return ClrPlugin;
            if (s.StartsWith("[")) return ClrPlugin;
            return ClrNormal;
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