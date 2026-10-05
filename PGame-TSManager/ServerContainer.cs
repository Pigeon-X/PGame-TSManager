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
using System.Windows;
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
        private StreamWriter? _stdin;

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

        // 配色由 App.ApplyTheme() 注入（跟随 TShock 控制台色 + 系统深浅色）
        private static Brush Res(string key, Color fallback)
            => (Application.Current?.TryFindResource(key) as Brush) ?? new SolidColorBrush(fallback);
        private static Brush ClrNormal => Res("LogNormal", Color.FromRgb(0xD6, 0xD6, 0xD6));
        private static Brush ClrDim    => Res("TextDim",   Color.FromRgb(0x8A, 0x8A, 0x8A));
        private static Brush ClrGood   => Res("LogOk",     Color.FromRgb(0x66, 0xBB, 0x6A));
        private static Brush ClrWarn   => Res("LogWarn",   Color.FromRgb(0xFF, 0xB7, 0x4D));
        private static Brush ClrError  => Res("LogError",  Color.FromRgb(0xFF, 0x52, 0x52));
        private static Brush ClrAccent => Res("LogCmd",    Color.FromRgb(0x4F, 0xC3, 0xF7));
        private static Brush ClrPlugin => Res("LogPlugin", Color.FromRgb(0x8B, 0x5C, 0xF6));
        private static Brush ClrInfo   => Res("LogInfo",   Color.FromRgb(0x4F, 0xC3, 0xF7));
        private static Brush ClrCmd    => Res("LogCmd",    Color.FromRgb(0x4C, 0x8D, 0xF6));

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

        /// <summary>
        /// 该服的运行目录（沙箱）。TShock 6.2 的插件目录认「exe 所在目录」，
        /// 所以要做到「每服只加载自己那套插件」，每服必须有自己的 exe 目录。
        /// 这里用 硬链接(exe) + 目录联接(bin/i18n/runtimes/x64) 指回管理器根目录，
        /// 不会多占磁盘；ServerPlugins 是该服自己的实体目录。
        /// </summary>
        private string RuntimeDirectory => Path.Combine(RootDirectory, "_runtime", Name);

        /// <summary>建立/修补该服的运行沙箱（幂等）。</summary>
        private void EnsureRuntimeSandbox()
        {
            var rt = RuntimeDirectory;
            var root = RootDirectory;
            Directory.CreateDirectory(rt);

            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;
            LinkFile(Path.Combine(root, exeName), Path.Combine(rt, exeName));
            LinkFile(Path.Combine(root, "GeoIP.dat"), Path.Combine(rt, "GeoIP.dat"));
            foreach (var d in new[] { "bin", "i18n", "runtimes", "x64" })
                LinkDirectory(Path.Combine(root, d), Path.Combine(rt, d));

            Directory.CreateDirectory(Path.Combine(rt, "ServerPlugins"));
            Directory.CreateDirectory(Path.Combine(rt, "Logs"));
        }

        private static void LinkFile(string target, string link)
        {
            if (File.Exists(link) || !File.Exists(target)) return;
            if (RunCmd($"mklink /H \"{link}\" \"{target}\"") && File.Exists(link)) return;
            try { File.Copy(target, link, true); } catch { }
        }

        private static void LinkDirectory(string target, string link)
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
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var pr = Process.Start(psi);
                if (pr == null) return false;
                pr.WaitForExit(15000);
                return pr.ExitCode == 0;
            }
            catch { return false; }
        }

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

            // 建立该服自己的运行沙箱（exe/bin 用链接指回根目录，不额外占空间）
            EnsureRuntimeSandbox();
            var runtimeDirectory = RuntimeDirectory;

            // 按该服 config.json 的「插件」清单，从总库 Plugins 同步到该服自己的 ServerPlugins
            if (_managerConfig.syncPluginsOnStart)
            {
                try
                {
                    PluginSync.Apply(runtimeDirectory, ManagerConfig.Resolve(_managerConfig.pluginDir),
                        _profile.ResolvePlugins(manifest),
                        manifest.PrunePlugins ?? _managerConfig.prunePlugins,
                        _managerConfig.disabledPluginDir, AddText);
                }
                catch (Exception ex) { AddText($"[插件同步] 失败：{ex.Message}\n"); }
            }

            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;
            var executable = Path.Combine(runtimeDirectory, exeName);
            if (!File.Exists(executable)) throw new FileNotFoundException($"找不到服务端可执行文件：{executable}");

            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = BuildArguments(tshockDir, manifest, root),
                // ★ 工作目录 = 本服沙箱：ServerLog.txt / Logs 各服各的
                //   插件目录 = exe 所在目录 = 本服沙箱 → 每服只加载自己清单里的插件
                WorkingDirectory = runtimeDirectory,
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
            // 直接往服务器进程的 stdin 写指令（和旧版 TSM 一样），不依赖 REST
            try { _stdin = _process.StandardInput; _stdin.AutoFlush = true; } catch { }

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

        // ---------- 发指令：往服务器控制台注入按键（不依赖 REST / PGameAPI / stdin） ----------
        public void SendText(string msg)
        {
            var process = _process;
            if (process == null) throw new InvalidOperationException("服务器未运行。");
            if (ConsoleInjector.Send((uint)process.Id, msg, out var err))
            {
                AddText($"> {msg}\n");
            }
            else
            {
                AddText($"[指令发送失败] {err}\n");
            }
        }

        // ---------- 彩色文本 ----------
        /// <summary>
        /// 控制台着色规则（尽量让每一类行都有颜色）：
        ///   错误=红  警告=琥珀  成功/状态=绿  键值/INFO=青  插件标签=紫  指令=蓝  列表/次要=灰
        /// </summary>
        private static Brush ColorFor(string line)
        {
            var s = line.TrimStart();
            if (s.Length == 0) return ClrNormal;

            // 管理器发出的指令回显
            if (s.StartsWith(">")) return ClrCmd;

            // 真错误（用具体特征，避免 ExceptionProbe / HotReload 这类名字被误判）
            if (s.Contains("Exception:") || s.Contains("Unhandled exception") ||
                s.Contains("致命") || s.Contains("错误") || s.Contains("失败") ||
                s.Contains("Error:") || s.Contains("ERROR:")) return ClrError;

            // 警告 / 缺失
            if (s.Contains("警告") || s.Contains("Warning") || s.Contains("WARN") ||
                s.Contains("已跳过") || s.Contains("找不到") || s.Contains("未找到") ||
                s.Contains("不存在")) return ClrWarn;

            // 关键成功状态
            if (s.Contains("服务器已启动") || s.Contains("正在侦听") || s.Contains("插件同步") ||
                s.Contains("[启动]") || s.Contains("总库齐备") || s.Contains("已加载") ||
                s.Contains("已启用") || s.Contains("已初始化") || s.Contains("已注册") ||
                s.Contains("已创建") || s.Contains("已开启") || s.Contains("已刷新")) return ClrGood;

            // 提示 / 说明
            if (s.StartsWith(":") || s.Contains("输入“help”") || s.Contains("输入\"help\"") ||
                s.Contains("DisableUUIDLogin") || s.Contains("UUID")) return ClrInfo;

            // 插件相关
            if (s.StartsWith("[Server API]")) return ClrInfo;
            if (s.StartsWith("[")) return ClrPlugin;

            // 键值行：xxx: yyy
            var ci = s.IndexOf(':');
            if (ci > 0 && ci <= 20) return ClrInfo;

            // 逗号结尾 = 列表项（权限表等）
            if (s.EndsWith(",")) return ClrDim;

            // 纯数字/坐标/百分比之类的进度行
            if (s.Contains("%")) return ClrDim;

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