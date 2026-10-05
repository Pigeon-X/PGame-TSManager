using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;
using PGameTSManager.Annotations;

namespace PGameTSManager
{
    public class ServerContainer : INotifyPropertyChanged
    {
        private readonly ManagerConfig _managerConfig;
        private readonly ServerProfile _profile;
        private readonly Paragraph _para;
        private Process? _process;
        private StreamWriter? _consoleLog;
        private StreamWriter? _stdin;

        private StreamWriter? OpenConsoleLog(string runtimeDirectory)
        {
            try
            {
                var sw = new StreamWriter(Path.Combine(runtimeDirectory, "console.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
                sw.WriteLine("[PGame-TSManager] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 开始捕获输出");
                return sw;
            }
            catch { return null; }
        }

        private void LogAndAdd(string? line)
        {
            if (line == null) return;
            try { _consoleLog?.WriteLine(line); } catch { }
            AddText(line + "\n");
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
                        try { process.Kill(); } catch { }
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

        /// <summary>服务器配置目录：1.PigeonServers\&lt;服&gt;（只放 config.json + tshock）。</summary>
        private string ServerDirectory => _profile.IsProfileMode
            ? _profile.ResolvedRootPath
            : Path.GetFullPath(Path.Combine(ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name));

        /// <summary>运行沙箱：exe / bin / ServerPlugins / server.properties / Logs 都放这里。</summary>
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

            var manifest = _profile.LoadManifest();
            if (manifest == null)
            {
                throw new FileNotFoundException($"找不到该服的 config.json：{Path.Combine(ServerDirectory, ServerProfile.ManifestFileName)}");
            }

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
            var showWindow = _managerConfig.showServerWindow;

            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = showWindow,
                CreateNoWindow = !showWindow
            };
            if (!showWindow)
            {
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardInputEncoding = new UTF8Encoding(false);
            }

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.Exited += (_, _) =>
            {
                var code = _process != null && _process.HasExited ? _process.ExitCode : -999;
                try { _consoleLog?.WriteLine("---process exited with code = " + code + "---"); } catch { }
                AddText($"---process exited with code = {code}---\n");
                _process?.Dispose();
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
            };
            if (!showWindow)
            {
                _consoleLog = OpenConsoleLog(runtimeDirectory);
                _process.OutputDataReceived += (_, args) => LogAndAdd(args.Data);
                _process.ErrorDataReceived += (_, args) => LogAndAdd(args.Data);
            }

            AddText($"[启动] {Name}  →  {executable}\n");
            AddText($"[启动] 参数 {arguments}\n");
            _process.Start();
            if (!showWindow)
            {
                // 必须一直持有 stdin 的 StreamWriter：否则一旦被回收/关闭，
                // TShock 的控制台读线程会读到 EOF 并“干净退出”(exit 0)
                _stdin = _process.StandardInput;
                _stdin.AutoFlush = true;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _stdin.WriteLine();
            }

            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>命令行参数：-config &lt;本服 properties&gt; -port .. -lang .. [-pass ..] [-maxplayers ..] [额外参数]</summary>
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

        /// <summary>生成该服的 server.properties，把 TShock 配置目录指向它自己的 tshock\，世界指向共享 Worlds\。</summary>
        private void WriteServerProperties(string runtimeDirectory, string serverDirectory, ServerManifest manifest)
        {
            var worlds = ManagerConfig.Resolve(_managerConfig.worldDir);
            var tshockDir = Path.Combine(serverDirectory, "tshock");
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

            Directory.CreateDirectory(ManagerConfig.Resolve(_managerConfig.worldDir));
            Directory.CreateDirectory(tshockDir);
        }

        /// <summary>建立运行沙箱：硬链接 exe/GeoIP.dat，目录联接 bin/i18n/runtimes/x64（不占额外空间）。</summary>
        private void EnsureRuntimeSandbox(string runtimeDirectory, string serverDirectory)
        {
            Directory.CreateDirectory(runtimeDirectory);
            var master = ManagerConfig.Resolve(_managerConfig.sharedRuntimeDir);
            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;

            LinkFile(Path.Combine(master, exeName), Path.Combine(runtimeDirectory, exeName));
            LinkFile(Path.Combine(master, "GeoIP.dat"), Path.Combine(runtimeDirectory, "GeoIP.dat"));
            foreach (var d in new[] { "bin", "i18n", "runtimes", "x64" })
            {
                LinkDirectory(Path.Combine(master, d), Path.Combine(runtimeDirectory, d));
            }
            // 沙箱里的 tshock\ 用目录联接指到本服真实配置目录，
            // 这样 TShock 的 tshock\config.json、sscconfig.json、插件数据都落在 1.PigeonServers\<服>\tshock\
            var realTshock = Path.Combine(serverDirectory, "tshock");
            Directory.CreateDirectory(realTshock);
            var linkTshock = Path.Combine(runtimeDirectory, "tshock");
            if (Directory.Exists(linkTshock))
            {
                var item = new DirectoryInfo(linkTshock);
                if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // 之前误建的实体目录：只删空壳（内容属于真实目录，不要动真实目录）
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

        /// <summary>启动前备份该服的 TShock 配置。</summary>
        private void BackupServerFiles(string serverDirectory)
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var safeName = SanitizeFileName(Name);
                var profileBackupDir = Path.Combine(ManagerConfig.Resolve(_managerConfig.backupDir), safeName);
                var target = Path.Combine(profileBackupDir, stamp);
                var candidates = new[]
                {
                    Path.Combine(serverDirectory, "tshock", _managerConfig.configFile),
                    Path.Combine(serverDirectory, "tshock", "sscconfig.json")
                };
                var copied = false;
                foreach (var file in candidates)
                {
                    if (!File.Exists(file)) continue;
                    Directory.CreateDirectory(target);
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    copied = true;
                }
                if (copied) PruneBackups(profileBackupDir, _managerConfig.backupKeep);
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

        private void AddText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var dispatcher = _para.Dispatcher;
            if (dispatcher == null) return;
            try { dispatcher.BeginInvoke(new Action(() => AppendText(text))); }
            catch { }
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

        public void SendText(string msg)
        {
            var process = _process;
            if (process == null) throw new InvalidOperationException("服务器未运行。");
            if (!process.StartInfo.RedirectStandardInput || _stdin == null)
            {
                AddText("[提示] 当前是“可见窗口”模式，请直接在服务器窗口里输入指令。\n");
                return;
            }
            AddText($"{msg}\n");
            _stdin.WriteLine(msg);
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