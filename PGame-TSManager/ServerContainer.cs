using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json;
using PGameTSManager.Annotations;

namespace PGameTSManager
{
    internal sealed class WorldBuildPlan
    {
        public int AutoCreate { get; init; }
        public string Difficulty { get; init; } = "";
        public string WorldEvil { get; init; } = "";
        public string WorldName { get; init; } = "";
        public string Seed { get; init; } = "";
        public int SeedFlags { get; init; }

        // 固定优先级：seed_* 组合值 > seed 复合串 > 随机。
        public string EffectiveSeed => SeedFlags > 0 ? SeedFlags.ToString(CultureInfo.InvariantCulture) : Seed;
    }

    /// <summary>
    /// rebuild 只读预演结果（dryRun）。只解析清单/世界路径/生成参数，不产生任何文件或进程副作用。
    /// </summary>
    public sealed class RebuildPreview
    {
        public string ServerId { get; init; } = "";
        public string WorldPath { get; init; } = "";
        public string WorldName { get; init; } = "";
        public bool WorldExists { get; init; }
        public long WorldSize { get; init; }
        public string WouldBackupTo { get; init; } = "";
        public int AutoCreate { get; init; }
        public string Difficulty { get; init; } = "";
        public string WorldEvil { get; init; } = "";
        public string Seed { get; init; } = "";
        public bool ServerPropertiesExists { get; init; }
    }

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
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private readonly ManagerConfig _managerConfig;
        private readonly ServerProfile _profile;
        private readonly Paragraph _para;
        private Process? _process;
        private StreamWriter? _stdin;

        // —— 看门狗状态 ——
        private bool _userStopRequested;                       // 手动停止 → 不自动重启
        private bool _watchdogRestarting;                      // 正在由看门狗重启（此时不要清零计数）
        private int _restartCount;                             // 连续自动重启次数
        private DateTime _startedAt = DateTime.UtcNow;
        private DateTime _lastLogAlertUtc = DateTime.MinValue;
        private DateTime _idleSinceUtc = DateTime.MinValue;
        private DateTime _lastIdleTrimUtc = DateTime.MinValue;
        private bool _serverReady;
        private bool _restProbeConfigured;
        private bool _restProbeOk;
        private int _healthFailureCount;
        private int _lastKnownPlayerCount = -1;
        private bool _deadServerRestartRequested;
        private DateTime _lastHealthyUtc = DateTime.MinValue;
        private DateTime _watchdogSuppressUntilUtc = DateTime.MinValue;
        private int _severeLogCount;
        private DateTime _severeLogWindowStartUtc = DateTime.MinValue;
        private DateTime _lastHealthAlertUtc = DateTime.MinValue;

        /// <summary>告警事件：(服, 标题, 详情)。由主窗口转给 Alerter。</summary>
        public event Action<ServerContainer, string, string>? OnAlert;

        public bool IsRunning
        {
            get => _process != null;
            set
            {
                if (value)
                {
                    // 进程已经被杀但 Exited 回调还没跑完时，_process 仍非空 → 直接 Start 会被跳过。
                    // 这里先把「已经退出」的对象清掉，避免"点了启动却什么都没发生"。
                    var dead = _process;
                    if (dead != null)
                    {
                        try { if (dead.HasExited) { dead.Dispose(); _process = null; } } catch { }
                    }
                    if (!_watchdogRestarting) _restartCount = 0;   // ★ 人工启动 = 重置崩溃计数
                    if (!IsRunning) { Start(); OnPropertyChanged(nameof(IsRunning)); }
                }
                else
                {
                    var process = _process;
                    if (process != null)
                    {
                        _userStopRequested = true;                 // ★ 手动停止：看门狗不要自动拉起
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

        private int _playerCount = -1;
        private int _maxPlayers;
        private string _uptime = "";

        /// <summary>在线人数（-1 = 还没有数据 / 没起来）。</summary>
        public int PlayerCount
        {
            get => _playerCount;
            private set
            {
                if (_playerCount == value) return;
                _playerCount = value;
                OnPropertyChanged(nameof(PlayerCount));
                OnPropertyChanged(nameof(PlayerText));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public int MaxPlayers
        {
            get => _maxPlayers;
            private set
            {
                if (_maxPlayers == value) return;
                _maxPlayers = value;
                OnPropertyChanged(nameof(MaxPlayers));
                OnPropertyChanged(nameof(PlayerText));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string Uptime
        {
            get => _uptime;
            private set { if (_uptime == value) return; _uptime = value; OnPropertyChanged(nameof(Uptime)); }
        }

        /// <summary>下拉框里显示的在线人数，例如 "12/252"。</summary>
        public string PlayerText => _playerCount < 0 ? "" : $"{_playerCount}/{_maxPlayers}";

        public string StatusText => IsRunning
            ? (_playerCount >= 0 ? $"运行中 · {_playerCount}/{_maxPlayers}" : "运行中")
            : "已停止";

        /// <summary>
        /// 轮询该服 REST 的 /v2/server/status，取在线人数 / 上限 / 运行时长。
        /// 由管理器界面定时调用（只在运行中才请求）。
        /// </summary>
        public async Task RefreshStatusAsync()
        {
            if (!IsRunning)
            {
                if (_playerCount != -1) { PlayerCount = -1; Uptime = ""; }
                return;
            }

            var port = RestPort;
            if (port <= 0) { _restProbeConfigured = false; return; }
            var token = ReadRestToken();
            if (string.IsNullOrEmpty(token)) { _restProbeConfigured = false; return; }
            _restProbeConfigured = true;

            try
            {
                var url = $"http://127.0.0.1:{port}/v2/server/status?token={Uri.EscapeDataString(token)}";
                var json = await Http.GetStringAsync(url);
                var o = JsonConvert.DeserializeObject<dynamic>(json);
                if (o == null) return;

                var pc = o.playercount;
                var mp = o.maxplayers;
                var up = o.uptime;
                var wasReady = _serverReady;
                if (pc != null) PlayerCount = Convert.ToInt32(pc.ToString());
                if (mp != null) MaxPlayers = Convert.ToInt32(mp.ToString());
                if (up != null) Uptime = up.ToString();
                _lastKnownPlayerCount = PlayerCount;
                _restProbeOk = true;
                _lastHealthyUtc = DateTime.UtcNow;
                _serverReady = true;
                _healthFailureCount = 0;
                if (!wasReady)
                {
                    ControlEventHub.Publish("server.state", StableId, new { state = "running", pid = SafeProcessId() });
                    ControlEventHub.Publish("server.ready", StableId, new { Name, GamePort, RestPort });
                }
            }
            catch
            {
                // 服务器可能正在加载世界 / 已停止：忽略即可
                _restProbeOk = false;
                if (PlayerCount != -1) PlayerCount = -1;
            }
        }

        /// <summary>
        /// 空服内存压缩：只在确认在线人数为 0 时执行，端口和进程保持运行。
        /// 玩家第一次进入不会遇到连接拒绝，最多由系统按需把页换回内存。
        /// </summary>
        public void TickIdleMemory()
        {
            if (!_managerConfig.idleMemoryTrimEnabled) return;
            if (!IsRunning || PlayerCount != 0)
            {
                _idleSinceUtc = DateTime.MinValue;
                return;
            }

            var now = DateTime.UtcNow;
            if (_idleSinceUtc == DateTime.MinValue)
            {
                _idleSinceUtc = now;
                return;
            }

            var idleMinutes = Math.Max(1, _managerConfig.idleMemoryTrimMinutes);
            if ((now - _idleSinceUtc).TotalMinutes < idleMinutes) return;

            var cooldownMinutes = Math.Max(1, _managerConfig.idleMemoryTrimCooldownMinutes);
            if (_lastIdleTrimUtc != DateTime.MinValue &&
                (now - _lastIdleTrimUtc).TotalMinutes < cooldownMinutes) return;

            var process = _process;
            if (process == null) return;

            try
            {
                var minBytes = Math.Max(32, _managerConfig.idleMemoryTrimMinWorkingSetMB) * 1024L * 1024L;
                if (process.WorkingSet64 < minBytes) return;

                var before = process.WorkingSet64 / 1024L / 1024L;
                if (!IdleMemoryTrim.TryTrim(process)) return;
                _lastIdleTrimUtc = now;
                var after = process.WorkingSet64 / 1024L / 1024L;
                AddText($"[内存压缩] 空服 {Name}：{before} MB → {after} MB（端口保持监听）\n");
            }
            catch
            {
                // 进程可能刚好退出或正在保存世界，忽略本轮。
            }
        }

        /// <summary>
        /// 看门狗健康检查：只处理意外情况，手动停止不会进入这里。
        /// 触发条件：游戏端口消失、REST 连续失效、日志连续致命异常。
        /// </summary>
        public void TickWatchdog()
        {
            if (!_managerConfig.watchdogEnabled || !IsRunning ||
                _userStopRequested || _watchdogRestarting || _deadServerRestartRequested)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now < _watchdogSuppressUntilUtc)
            {
                _healthFailureCount = 0;
                _lastHealthyUtc = now;
                return;
            }
            var uptime = now - _startedAt;
            var grace = Math.Max(10, _managerConfig.watchdogStartupGraceSeconds);
            if (!_serverReady && uptime.TotalSeconds < grace) return;

            var gamePortOk = GamePort <= 0 || IsPortListening(GamePort);
            var restOk = !_restProbeConfigured || _restProbeOk;
            if (gamePortOk && restOk)
            {
                _healthFailureCount = 0;
                _lastHealthyUtc = now;
                _serverReady = true;
                return;
            }

            _healthFailureCount++;
            var threshold = Math.Max(1, _managerConfig.watchdogHealthFailureThreshold);
            var unhealthySeconds = Math.Max(120, _managerConfig.watchdogUnhealthySeconds);
            if (_healthFailureCount < threshold ||
                (now - _lastHealthyUtc).TotalSeconds < unhealthySeconds) return;

            var detail = $"游戏端口={GamePort}:{(gamePortOk ? "正常" : "失效")}，" +
                         $"REST={( _restProbeConfigured ? (_restProbeOk ? "正常" : "失效") : "未配置")}，" +
                         $"最近在线={Math.Max(0, _lastKnownPlayerCount)}";

            // 端口仍在监听时，REST 失败通常只是接口暂时不可用，绝不当死服重启。
            if (gamePortOk)
            {
                if ((now - _lastHealthAlertUtc).TotalSeconds >= 60)
                {
                    _lastHealthAlertUtc = now;
                    RaiseAlert($"服务器健康检查异常：{Name}", detail + "；游戏端口仍正常，暂不重启。");
                }
                _healthFailureCount = 0;
                return;
            }

            // REST 仍活着说明进程还在工作，换图/热重载期间只告警不杀进程。
            if (_restProbeConfigured && _restProbeOk)
            {
                if ((now - _lastHealthAlertUtc).TotalSeconds >= 60)
                {
                    _lastHealthAlertUtc = now;
                    RaiseAlert($"服务器健康检查异常：{Name}", detail + "；REST 仍正常，暂不重启。");
                }
                return;
            }

            RequestDeadServerRestart("检测到服务器进程存活但已不可用", detail);
        }

        private void RequestDeadServerRestart(string title, string detail)
        {
            if (_deadServerRestartRequested || _userStopRequested || !IsRunning) return;
            if (DateTime.UtcNow < _watchdogSuppressUntilUtc) return;
            _deadServerRestartRequested = true;
            _serverReady = false;
            AddText($"[看门狗] {title}：{detail}\n");
            RaiseAlert($"服务器判断为死服，准备重启：{Name}", detail);

            var process = _process;
            if (process == null) return;
            try { process.Kill(true); }
            catch { try { process.Kill(); } catch { } }
        }

        public Brush Foreground { get; private set; }
        public Brush Background { get; private set; }

        /// <summary>该服的配置目录（1.PigeonServers\&lt;序号.名字&gt;）。</summary>
        public string ProfileDirectory => ServerDirectory;
        /// <summary>下拉框显示用：带序号，例如 "1. 生存"（序号来自服务器目录名）。</summary>
        public string ListLabel
        {
            get
            {
                var n = 0;
                try
                {
                    var dir = ServerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    n = ManagerConfig.OrdinalOf(Path.GetFileName(dir));
                }
                catch { }
                return n > 0 ? n + ". " + Name : Name;
            }
        }
        public string Name => string.IsNullOrWhiteSpace(_profile.name)
            ? Path.GetFileName(ServerDirectory.TrimEnd(Path.DirectorySeparatorChar))
            : _profile.name;
        public string StableId => _profile.StableId;
        public string WorldName => _profile.LoadManifest()?.World ?? "";

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

        /// <summary>该服的对外端口（1.PigeonServers\&lt;服&gt;\config.json 的「端口」）。</summary>
        public int GamePort
        {
            get { try { return _profile.LoadManifest()?.Port ?? 0; } catch { return 0; } }
        }

        /// <summary>该服的 REST 端口。</summary>
        public int RestPort
        {
            get { try { return _profile.LoadManifest()?.RestPort ?? 0; } catch { return 0; } }
        }

        public int ProcessId
        {
            get { try { return _process?.Id ?? 0; } catch { return 0; } }
        }

        public long MemoryBytes
        {
            get { try { return _process?.WorkingSet64 ?? 0; } catch { return 0; } }
        }

        public string TodayLogPath => Path.Combine(
            ManagerConfig.Resolve(_managerConfig.logDir),
            SanitizeFileName(Name) + "-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

        public string WorldPath
        {
            get
            {
                try
                {
                    var manifest = _profile.LoadManifest();
                    var world = manifest?.World ?? "";
                    if (string.IsNullOrWhiteSpace(world)) return "";
                    return Path.IsPathRooted(world)
                        ? world
                        : Path.Combine(ManagerConfig.Resolve(_managerConfig.worldDir), world);
                }
                catch { return ""; }
            }
        }

        public long WorldSizeBytes
        {
            get
            {
                try { return File.Exists(WorldPath) ? new FileInfo(WorldPath).Length : 0; }
                catch { return 0; }
            }
        }

        public int PluginCount
        {
            get
            {
                try
                {
                    var dir = Path.Combine(RuntimeDirectory, "ServerPlugins");
                    return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.dll").Length : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>往本服自己的日志面板写一行（顺序启动等外部流程用）。</summary>
        public void Log(string text) => AddText(text);

        /// <summary>
        /// 等本服「真正起来」：进程还活着 + 游戏端口进入监听（世界加载完、开始收玩家）。
        /// 端口通了之后再稳一小会儿，让插件把初始化跑完。
        /// </summary>
        public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, int settleMs = 2500)
        {
            var port = GamePort;
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var proc = _process;
                if (proc == null) return false;                       // 已经退出 = 启动失败
                try { if (proc.HasExited) return false; } catch { return false; }
                if (port > 0 && IsPortListening(port))
                {
                    if (settleMs > 0) await Task.Delay(settleMs);
                    proc = _process;
                    if (proc == null) return false;
                    try { return !proc.HasExited; } catch { return false; }
                }
                await Task.Delay(600);
            }
            return false;
        }

        /// <summary>
        /// 等本服进程真正退出（停止后立刻启动会抢不到端口 / 被当成"已经在运行"跳过）。
        /// </summary>
        public async Task WaitForStoppedAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && IsRunning)
                await Task.Delay(250);
        }

        /// <summary>
        /// 退出 TSM 时的同步停服：先请求 TShock /stop，超时后强杀整棵进程树，确保不残留服务器进程。
        /// </summary>
        public void StopForExit(TimeSpan gracefulTimeout)
        {
            var process = _process;
            if (process == null) return;

            _userStopRequested = true;
            try
            {
                var stopTask = Task.Run(() =>
                {
                    string output;
                    return SendCommandViaRest("/stop", out output);
                });
                stopTask.Wait(TimeSpan.FromSeconds(3));
            }
            catch { }

            try
            {
                if (!process.WaitForExit((int)Math.Max(1000, gracefulTimeout.TotalMilliseconds)))
                {
                    try { process.Kill(true); } catch { try { process.Kill(); } catch { } }
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                try { process.Kill(true); } catch { try { process.Kill(); } catch { } }
            }
            finally
            {
                try { _stdin?.Dispose(); } catch { }
                _stdin = null;
                try { process.Dispose(); } catch { }
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// 立即把插件总库按本服清单同步进**运行沙箱** ServerPlugins（而不是根目录那份）。
        /// 运行中调用也安全：TShock 用影子加载（Assembly.Load(byte[])），沙箱 DLL 不被锁；
        /// 同步成功后再配合 `/hr load &lt;插件&gt;` 即可热升级插件，无需重启。
        /// </summary>
        public string SyncPluginsNow()
        {
            var manifest = _profile.LoadManifest();
            var runtimeDirectory = RuntimeDirectory;
            Directory.CreateDirectory(runtimeDirectory);
            var summary = PluginSync.Apply(
                runtimeDirectory,
                ManagerConfig.Resolve(_managerConfig.pluginDir),
                _profile.ResolvePlugins(manifest),
                manifest?.PrunePlugins ?? _managerConfig.prunePlugins,
                _managerConfig.disabledPluginDir,
                message =>
                {
                    foreach (var line in message.Split('\n'))
                    {
                        var text = line.Trim();
                        if (text.Length > 0) AddText(text + "\n");
                    }
                });
            return summary;
        }

        /// <summary>
        /// 只读世界重建预演：解析清单、世界路径与生成参数，绝不改文件、停服、删图。
        /// 供 Control API 的 dryRun 使用，让外部项目能安全校验 rebuild 参数。
        /// </summary>
        public RebuildPreview PreviewRebuild(string requestId, string actor)
        {
            var manifest = _profile.LoadManifest()
                ?? throw new InvalidOperationException("找不到服务器清单 config.json");
            var worldPath = ResolveWorldPath(manifest);
            if (string.IsNullOrWhiteSpace(worldPath))
                throw new InvalidOperationException("该服没有配置世界文件");

            var plan = ReadWorldBuildPlan(RuntimeDirectory, manifest);
            var worldExists = File.Exists(worldPath);
            var preview = new RebuildPreview
            {
                ServerId = StableId,
                WorldPath = worldPath,
                WorldName = plan.WorldName,
                WorldExists = worldExists,
                WorldSize = worldExists ? new FileInfo(worldPath).Length : 0,
                WouldBackupTo = worldExists ? worldPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") : "",
                AutoCreate = plan.AutoCreate,
                Difficulty = plan.Difficulty,
                WorldEvil = plan.WorldEvil,
                Seed = plan.EffectiveSeed,
                ServerPropertiesExists = File.Exists(Path.Combine(RuntimeDirectory, "server.properties"))
            };

            ControlEventHub.Publish("world.rebuild.dryrun", StableId, new
            {
                requestId,
                actor,
                preview.WorldPath,
                preview.AutoCreate,
                difficulty = preview.Difficulty,
                worldevil = preview.WorldEvil,
                seed = string.IsNullOrWhiteSpace(preview.Seed) ? "<random>" : preview.Seed,
                preview.WorldExists,
                preview.WorldSize,
                preview.ServerPropertiesExists
            });
            return preview;
        }

        /// <summary>
        /// 由本地 Control API 调用的世界重建入口。TSM 是 server.properties / *.wld 的唯一写入者。
        /// </summary>
        public async Task RebuildWorldAsync(string requestId, string actor)
        {
            var manifest = _profile.LoadManifest()
                ?? throw new InvalidOperationException("找不到服务器清单 config.json");
            var worldPath = ResolveWorldPath(manifest);
            if (string.IsNullOrWhiteSpace(worldPath))
                throw new InvalidOperationException("该服没有配置世界文件");

            ControlEventHub.Publish("world.rebuild.started", StableId, new { requestId, actor, worldPath });

            if (IsRunning)
            {
                _userStopRequested = true;
                IsRunning = false;
                await WaitForStoppedAsync(TimeSpan.FromSeconds(60));
            }

            BackupServerFiles(ServerDirectory);
            if (File.Exists(worldPath))
            {
                var worldBackup = worldPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(worldPath, worldBackup, true);
                ControlEventHub.Publish("world.rebuild.backup_created", StableId, new
                {
                    requestId,
                    path = worldBackup,
                    size = new FileInfo(worldBackup).Length
                });
            }

            UpdateServerPropertiesForRebuild(RuntimeDirectory, manifest, worldPath);
            ControlEventHub.Publish("world.rebuild.generating", StableId, new
            {
                requestId,
                worldPath,
                autoCreate = manifest.AutoCreate ?? 0
            });

            if (File.Exists(worldPath)) File.Delete(worldPath);
            IsRunning = true;

            var ready = await WaitUntilReadyAsync(TimeSpan.FromSeconds(Math.Max(300, _managerConfig.watchdogWorldBuildSuppressSeconds + 120)));
            ControlEventHub.Publish(ready ? "world.rebuild.ready" : "world.rebuild.failed", StableId, new
            {
                requestId,
                worldPath,
                ready
            });
        }

        public string ResolveWorldPath(ServerManifest manifest)
        {
            var world = manifest.World ?? "";
            if (string.IsNullOrWhiteSpace(world)) return "";
            if (!world.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) world += ".wld";
            return Path.IsPathRooted(world)
                ? world
                : Path.Combine(ManagerConfig.Resolve(_managerConfig.worldDir), world);
        }

        private static void UpdateServerPropertiesForRebuild(string runtimeDirectory, ServerManifest manifest, string worldPath)
        {
            var propertiesPath = Path.Combine(runtimeDirectory, "server.properties");
            if (!File.Exists(propertiesPath)) return;

            var worldName = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(worldPath) : manifest.Name!;
            var lines = File.ReadAllLines(propertiesPath).ToList();
            SetProperty(lines, "world", worldPath);
            SetProperty(lines, "worldname", worldName);

            if (lines.Count == 0 || !lines[0].StartsWith("# 由 PGame-TSManager 维护", StringComparison.OrdinalIgnoreCase))
                lines.Insert(0, "# 由 PGame-TSManager 维护");
            File.WriteAllLines(propertiesPath, lines, new UTF8Encoding(false));
        }

        private static void SetProperty(List<string> lines, string key, string value)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }
            lines.Add(key + "=" + value);
        }

        /// <summary>日志里出现「会要命」的字眼就告警（每服 5 分钟最多一次，避免刷屏）。</summary>
        private bool IsSevereLine(string line)
        {
            try
            {
                var severe = line.Contains("Unhandled exception") || line.Contains("未处理的异常") ||
                             line.Contains("Startup aborted") || line.Contains("致命") ||
                             line.Contains("OutOfMemory") || line.Contains("StackOverflow") ||
                             line.Contains("Failed to load assembly");
                if (!severe) return false;
                if ((DateTime.UtcNow - _lastLogAlertUtc).TotalSeconds < 300) return false;
                _lastLogAlertUtc = DateTime.UtcNow;
                return true;
            }
            catch { return false; }
        }

        private void RecordWatchdogLogError(string line)
        {
            if (!_managerConfig.watchdogLogErrorRestartEnabled) return;
            var fatal =
                line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("StackOverflow", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Startup aborted", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Failed to load assembly", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("未处理的异常", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("致命", StringComparison.OrdinalIgnoreCase);
            if (!fatal) return;

            var now = DateTime.UtcNow;
            var window = TimeSpan.FromSeconds(Math.Max(10, _managerConfig.watchdogLogErrorWindowSeconds));
            if (_severeLogWindowStartUtc == DateTime.MinValue || now - _severeLogWindowStartUtc > window)
            {
                _severeLogWindowStartUtc = now;
                _severeLogCount = 0;
            }
            _severeLogCount++;

            var threshold = Math.Max(1, _managerConfig.watchdogLogErrorThreshold);
            var grace = Math.Max(10, _managerConfig.watchdogStartupGraceSeconds);
            if (_severeLogCount >= threshold && (_serverReady || (now - _startedAt).TotalSeconds >= grace))
            {
                _severeLogCount = 0;
                RequestDeadServerRestart("日志出现连续致命异常", line.Trim());
            }
        }

        private static readonly object FileLogGate = new();

        /// <summary>把控制台内容同时写到文件：&lt;程序目录&gt;\Logs\&lt;服&gt;-yyyyMMdd.log（便于事后排查/导出）。</summary>
        private void WriteToFile(string text)
        {
            try
            {
                var dir = ManagerConfig.Resolve(_managerConfig.logDir);
                Directory.CreateDirectory(dir);
                var safe = string.Join("_", Name.Split(Path.GetInvalidFileNameChars()));
                var file = Path.Combine(dir, safe + "-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                var line = text.Replace("\r\n", "\n").TrimEnd('\n');
                if (line.Length == 0) return;
                lock (FileLogGate)
                {
                    File.AppendAllText(file, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
        private void RaiseAlert(string title, string detail)
        {
            try { OnAlert?.Invoke(this, title, detail); } catch { }
        }
        /// <summary>只读探测：本机有没有人在监听这个端口（不建连接，不会污染服务器日志）。</summary>
        private static bool IsPortListening(int port)
        {
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(ep => ep.Port == port);
            }
            catch { return false; }
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
        private string RuntimeDirectory => Path.Combine(ManagerConfig.Resolve(_managerConfig.runtimeDir), Name);

        /// <summary>建立/修补该服的运行沙箱（幂等）。</summary>
        private void EnsureRuntimeSandbox(string serverDirectory)
        {
            var rt = RuntimeDirectory;
            var runtimeRoot = ManagerConfig.Resolve(_managerConfig.sharedRuntimeDir);
            Directory.CreateDirectory(rt);

            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;
            LinkFile(Path.Combine(runtimeRoot, exeName), Path.Combine(rt, exeName));
            LinkFile(Path.Combine(runtimeRoot, "GeoIP.dat"), Path.Combine(rt, "GeoIP.dat"));
            foreach (var d in new[] { "bin", "i18n", "runtimes", "x64" })
                EnsureJunction(Path.Combine(runtimeRoot, d), Path.Combine(rt, d));

            // ★ tshock 目录联接回本服真实配置目录。
            //   插件常常用相对路径 "tshock\xxx.json" 找配置；服务器目录改名后，
            //   旧联接会变成悬空链接（指向不存在的旧目录）→ 插件以为配置丢了 →
            //   有的插件（CustomPlayer）会尝试新建配置并 Console.ReadKey()，
            //   而我们是重定向控制台，直接抛异常把 TShock 启动中止。
            //   EnsureJunction 会检测指向并在需要时重建，改名后自动恢复。
            EnsureJunction(Path.Combine(serverDirectory, "tshock"), Path.Combine(rt, "tshock"));

            Directory.CreateDirectory(Path.Combine(rt, "ServerPlugins"));
            Directory.CreateDirectory(Path.Combine(rt, "Logs"));
        }

        /// <summary>
        /// 确保 link 是指向 target 的目录联接；指向别处（或已悬空）就删掉联接重建。
        /// 只删「联接本身」，绝不递归删目标内容。
        /// </summary>
        private static void EnsureJunction(string target, string link)
        {
            try
            {
                if (!Directory.Exists(target)) return;
                var want = Path.GetFullPath(target);

                var info = new DirectoryInfo(link);
                if (info.LinkTarget != null)
                {
                    var cur = info.LinkTarget;
                    if (cur != null && string.Equals(Path.GetFullPath(cur), want, StringComparison.OrdinalIgnoreCase)) return;
                    try { info.Delete(); } catch { return; }   // 悬空/指向别处 → 只删联接
                }
                else if (Directory.Exists(link))
                {
                    return;                                   // 已经是真目录，不动它
                }

                if (RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link)) return;
                try { Directory.CreateDirectory(link); } catch { }
            }
            catch { }
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
            EnsureRuntimeSandbox(serverDirectory);
            var runtimeDirectory = RuntimeDirectory;

            // 按该服 config.json 的「插件」清单，从总库 Plugins 同步到该服自己的 ServerPlugins
            if (_managerConfig.syncPluginsOnStart)
            {
                try
                {
                    PluginSync.Apply(runtimeDirectory, ManagerConfig.Resolve(_managerConfig.pluginDir),
                        _profile.ResolvePlugins(manifest),
                        manifest.PrunePlugins ?? _managerConfig.prunePlugins,
                        _managerConfig.disabledPluginDir, message => AddText(message));
                }
                catch (Exception ex) { AddText($"[插件同步] 失败：{ex.Message}\n"); }
            }

            var exeName = string.IsNullOrWhiteSpace(_profile.executable) ? _managerConfig.serverExecutable : _profile.executable;
            var executable = Path.Combine(runtimeDirectory, exeName);
            if (!File.Exists(executable)) throw new FileNotFoundException($"找不到服务端可执行文件：{executable}");
            KillResidualServerProcess(executable);
            var arguments = BuildArguments(tshockDir, manifest, root, out var autoCreateSize);

            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
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
            _process.ErrorDataReceived += (_, a) => { if (a.Data != null) AddText(a.Data + "\n", isError: true); };
            _process.Exited += (_, _) =>
            {
                var code = _process != null && _process.HasExited ? _process.ExitCode : -999;
                var wasUserStop = _userStopRequested;
                var wasMaintenanceExit = DateTime.UtcNow < _watchdogSuppressUntilUtc;
                var uptime = DateTime.UtcNow - _startedAt;

                AddText($"---服务器已退出（退出码 {code}）---\n");
                ControlEventHub.Publish("server.state", StableId, new
                {
                    state = "stopped",
                    exitCode = code,
                    manual = wasUserStop,
                    maintenance = wasMaintenanceExit
                });
                if (code != 0 && !wasUserStop && !wasMaintenanceExit)
                    ControlEventHub.Publish("server.crash", StableId, new
                    {
                        exitCode = code,
                        consecutive = _restartCount,
                        uptimeMinutes = Math.Round(uptime.TotalMinutes, 1)
                    });
                _process?.Dispose();
                _process = null;
                _userStopRequested = false;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusText));

                if (wasUserStop || wasMaintenanceExit)
                {
                    _restartCount = 0;
                    AddText(wasUserStop
                        ? "[看门狗] 这是手动停止，不自动重启\n"
                        : "[看门狗] 这是维护/换图操作期间退出，不自动重启\n");
                    return;
                }

                // TShock 的 /off、/exit、正常停机通常返回 0。
                // 即使日志没有被识别到，也不能把正常退出当成崩溃拉起。
                if (code == 0 && !_watchdogRestarting)
                {
                    _restartCount = 0;
                    AddText("[看门狗] 服务器正常退出（退出码 0），不自动重启\n");
                    return;
                }

                if (uptime.TotalSeconds >= _managerConfig.watchdogStableSeconds) _restartCount = 0;

                var max = Math.Max(0, _managerConfig.watchdogMaxRestarts);
                var exitInfo = $"退出码 {code}，运行时长 {uptime.TotalMinutes:0.0} 分钟";

                if (!_managerConfig.watchdogEnabled)
                {
                    AddText("[看门狗] 已关闭，不自动重启\n");
                    RaiseAlert($"服务器异常退出：{Name}", exitInfo + "；看门狗已关闭，未自动重启。");
                    return;
                }

                if (_restartCount >= max)
                {
                    ControlEventHub.Publish("server.giveup", StableId, new
                    {
                        attempts = _restartCount,
                        max,
                        exitCode = code
                    });
                    AddText($"[看门狗] 连续自动重启已达上限 {max} 次，停止重启（请人工检查）\n");
                    RaiseAlert($"服务器反复崩溃，已放弃自动重启：{Name}",
                        exitInfo + $"；连续自动重启 {_restartCount} 次达到上限 {max}，已停止自动重启，请人工检查原因。");
                    return;
                }

                _restartCount++;
                var delay = Math.Max(0, _managerConfig.watchdogRestartDelaySeconds);
                ControlEventHub.Publish("server.restart", StableId, new
                {
                    attempt = _restartCount,
                    max,
                    delaySeconds = delay,
                    exitCode = code
                });
                AddText($"[看门狗] {exitInfo}，{delay} 秒后自动重启（第 {_restartCount}/{max} 次）\n");
                RaiseAlert($"服务器异常退出，正在自动重启：{Name}",
                    exitInfo + $"；第 {_restartCount}/{max} 次自动重启（{delay} 秒后）。");

                Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ =>
                {
                    try
                    {
                        var dispatcher = _para.Dispatcher;
                        if (dispatcher == null) return;
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                _watchdogRestarting = true;
                                try
                                {
                                    if (!IsRunning) IsRunning = true;
                                    AddText($"[看门狗] 已发起第 {_restartCount}/{max} 次重启\n");
                                }
                                finally { _watchdogRestarting = false; }
                            }
                            catch (Exception ex) { AddText("[看门狗] 重启失败：" + ex.Message + "\n"); }
                        }));
                    }
                    catch { }
                });
            };

            AddText($"[启动] {Name}  (共享 ServerPlugins，无窗口)\n");
            _startedAt = DateTime.UtcNow;
            _lastHealthyUtc = _startedAt;
            _watchdogSuppressUntilUtc = DateTime.MinValue;
            _idleSinceUtc = DateTime.MinValue;
            _lastIdleTrimUtc = DateTime.MinValue;
            _serverReady = false;
            _restProbeConfigured = false;
            _restProbeOk = false;
            _healthFailureCount = 0;
            _lastKnownPlayerCount = -1;
            _deadServerRestartRequested = false;
            _severeLogCount = 0;
            _severeLogWindowStartUtc = DateTime.MinValue;
            _userStopRequested = false;
            if (autoCreateSize > 0)
            {
                var buildSeconds = WorldBuildSuppressSeconds(autoCreateSize);
                _lastHealthyUtc = DateTime.UtcNow;
                _watchdogSuppressUntilUtc = DateTime.UtcNow.AddSeconds(buildSeconds);
                AddText($"[看门狗] 世界缺失，已启用自动建图保护 {buildSeconds} 秒（autocreate={autoCreateSize}）\n");
            }
            _process.Start();
            ControlEventHub.Publish("server.state", StableId, new { state = "starting", pid = _process.Id });
            try { _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); } catch { }
            // 直接往服务器进程的 stdin 写指令（和旧版 TSM 一样），不依赖 REST
            try { _stdin = _process.StandardInput; _stdin.AutoFlush = true; } catch { }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>-config &lt;该服 tshock 目录&gt; -world &lt;共享世界文件&gt; -port .. -lang ..</summary>
        private string BuildArguments(string tshockDir, ServerManifest manifest, string root, out int autoCreateSize)
        {
            autoCreateSize = 0;
            var parts = new List<string>
            {
                "-config", Quote(tshockDir),
                "-port",   manifest.Port.ToString(CultureInfo.InvariantCulture),
                "-lang",   manifest.Language.ToString(CultureInfo.InvariantCulture)
            };

            var world = manifest.World ?? string.Empty;
            var worldPath = "";
            if (!string.IsNullOrWhiteSpace(world))
            {
                if (!world.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) world += ".wld";
                worldPath = Path.IsPathRooted(world) ? world : Path.Combine(ManagerConfig.Resolve(_managerConfig.worldDir), world);
                parts.Add("-world");
                parts.Add(Quote(worldPath));
            }

            if (!string.IsNullOrWhiteSpace(worldPath) && !File.Exists(worldPath))
            {
                var plan = ReadWorldBuildPlan(RuntimeDirectory, manifest);
                autoCreateSize = Math.Max(1, plan.AutoCreate);

                if (!HasArgument(_profile.arguments, "autocreate") &&
                    !HasArgument(manifest.Parameters, "autocreate"))
                {
                    parts.Add("-autocreate");
                    parts.Add(autoCreateSize.ToString(CultureInfo.InvariantCulture));
                }

                var explicitAuto = TryGetAutoCreateFromArguments(_profile.arguments);
                if (explicitAuto <= 0) explicitAuto = TryGetAutoCreateFromArguments(manifest.Parameters);
                if (explicitAuto > 0) autoCreateSize = explicitAuto;

                if (!HasArgument(_profile.arguments, "seed") && !HasArgument(manifest.Parameters, "seed"))
                {
                    var seed = plan.EffectiveSeed;
                    if (!string.IsNullOrWhiteSpace(seed))
                    {
                        parts.Add("-seed");
                        parts.Add(Quote(seed));
                    }
                }

                if (!HasArgument(_profile.arguments, "difficulty") && !HasArgument(manifest.Parameters, "difficulty") &&
                    !string.IsNullOrWhiteSpace(plan.Difficulty))
                {
                    parts.Add("-difficulty");
                    parts.Add(Quote(plan.Difficulty));
                }

                if (!HasArgument(_profile.arguments, "worldevil") && !HasArgument(manifest.Parameters, "worldevil") &&
                    !string.IsNullOrWhiteSpace(plan.WorldEvil))
                {
                    parts.Add("-worldevil");
                    parts.Add(Quote(plan.WorldEvil));
                }

                if (!HasArgument(_profile.arguments, "worldname") && !HasArgument(manifest.Parameters, "worldname") &&
                    !string.IsNullOrWhiteSpace(plan.WorldName))
                {
                    parts.Add("-worldname");
                    parts.Add(Quote(plan.WorldName));
                }

                AddText($"[建图参数] autocreate={autoCreateSize} difficulty={plan.Difficulty} worldevil={plan.WorldEvil} " +
                        $"seed={(string.IsNullOrWhiteSpace(plan.EffectiveSeed) ? "<随机>" : plan.EffectiveSeed)} worldname={plan.WorldName}\n");
            }

            if (manifest.MaxPlayers > 0) { parts.Add("-maxplayers"); parts.Add(manifest.MaxPlayers.ToString(CultureInfo.InvariantCulture)); }
            if (!string.IsNullOrWhiteSpace(manifest.Password)) { parts.Add("-pass"); parts.Add(Quote(manifest.Password!)); }
            if (!string.IsNullOrWhiteSpace(_profile.arguments)) parts.Add(_profile.arguments!);
            else if (!string.IsNullOrWhiteSpace(manifest.Parameters)) parts.Add(manifest.Parameters!);
            return string.Join(" ", parts);
        }

        private int WorldBuildSuppressSeconds(int autoCreateSize)
        {
            var minimum = autoCreateSize switch
            {
                1 => 300,
                2 => 480,
                3 => 600,
                _ => 600
            };
            return Math.Max(Math.Max(180, _managerConfig.watchdogWorldBuildSuppressSeconds), minimum);
        }

        private static WorldBuildPlan ReadWorldBuildPlan(string runtimeDirectory, ServerManifest manifest)
        {
            var props = ReadServerProperties(runtimeDirectory);
            var flags = 0;
            try
            {
                flags |= ReadSeedFlag(props, "seed_drunk", 1);
                flags |= ReadSeedFlag(props, "seed_notthebees", 2);
                flags |= ReadSeedFlag(props, "seed_fortheworthy", 4);
                flags |= ReadSeedFlag(props, "seed_celebration", 8);
                flags |= ReadSeedFlag(props, "seed_theconstant", 16);
                flags |= ReadSeedFlag(props, "seed_remix", 32);
                flags |= ReadSeedFlag(props, "seed_notraps", 64);
                flags |= ReadSeedFlag(props, "seed_zenith", 128);
                flags |= ReadSeedFlag(props, "seed_skyblock", 256);
            }
            catch { }

            var autoCreate = manifest.AutoCreate ?? GetInt(props, "autocreate", 1);
            return new WorldBuildPlan
            {
                AutoCreate = autoCreate,
                Difficulty = GetString(props, "difficulty"),
                WorldEvil = GetString(props, "worldevil"),
                WorldName = GetString(props, "worldname"),
                Seed = GetString(props, "seed"),
                SeedFlags = flags
            };
        }

        private static Dictionary<string, string> ReadServerProperties(string runtimeDirectory)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(runtimeDirectory, "server.properties");
                if (!File.Exists(path)) return result;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            catch { }
            return result;
        }

        private static int ReadSeedFlag(Dictionary<string, string> props, string key, int bit)
            => string.Equals(GetString(props, key), "1", StringComparison.OrdinalIgnoreCase) ? bit : 0;

        private static string GetString(Dictionary<string, string> props, string key)
            => props.TryGetValue(key, out var value) ? value : "";

        private static int GetInt(Dictionary<string, string> props, string key, int fallback)
        {
            var value = GetString(props, key);
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static bool HasArgument(string? arguments, string name)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return false;
            return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.Equals("-" + name, StringComparison.OrdinalIgnoreCase) ||
                          x.StartsWith("-" + name + "=", StringComparison.OrdinalIgnoreCase));
        }

        private static int TryGetAutoCreateFromArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return 0;
            var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i].Trim('"');
                if (token.Equals("-autocreate", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                {
                    return int.TryParse(tokens[i + 1].Trim('"'), out var size) ? size : 0;
                }
                if (token.StartsWith("-autocreate=", StringComparison.OrdinalIgnoreCase))
                {
                    return int.TryParse(token["-autocreate=".Length..].Trim('"'), out var size) ? size : 0;
                }
            }
            return 0;
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        public int? SafeProcessId()
        {
            try { return _process?.Id; } catch { return null; }
        }

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

        // ---------- 发指令 ----------
        // 走 TShock 官方 REST 的 rawcmd（已按 TShock 6.2.1 源码核对过）：
        //   POST /v3/server/rawcmd?token=<外部应用令牌>&cmd=<指令>
        //   返回 {"status":"200","response":["输出行", ...]}
        //   TShock 的 HandleCommand 要求指令带前导 '/'，所以这里自动补。
        //   ⚠ 旧写法 /v2/server/rawcmd 在 6.2.1 上不存在 → 必然 404（之前发不出指令就是这个原因）。
        // 另外两条路在 TShock 6.2 上都不可用，故不再使用：
        //   - 重定向 stdin：TShock 明确拒绝（"Input redirection is not supported"）
        //   - 控制台按键注入：写入成功但服务器不读取（输入被 OTAPI detour 接管）
        public void SendText(string msg)
        {
            if (_process == null) throw new InvalidOperationException("服务器未运行。");

            var cmd = (msg ?? "").Trim();
            if (cmd.Length == 0) return;
            if (cmd[0] != '/') cmd = "/" + cmd;          // TShock 要求前导斜杠
            ArmWatchdogSuppression(cmd);

            var restErr = TrySendViaRest(cmd);
            if (restErr == null) return;

            // 兜底：往服务器 stdin 写一行（部分版本/插件组合下有效）
            if (_stdin != null)
            {
                try { _stdin.WriteLine(cmd); AddText($"> {cmd}（stdin 兜底）\n"); return; } catch { }
            }

            AddText($"[指令发送失败] {restErr}\n");
        }

        /// <summary>REST /v3/server/rawcmd。成功返回 null，失败返回错误描述。</summary>
        private string? TrySendViaRest(string cmd)
        {
            var err = SendCommandViaRest(cmd, out var output);
            if (err == null) AddText($"> {cmd}\n{output}\n");
            return err;
        }

        /// <summary>
        /// 把一条指令通过该服的 TShock REST 送进服务器控制台。
        /// 不依赖界面、不依赖进程对象，可被 --send 命令行模式复用。
        /// 成功返回 null 且 output = 命令回显；失败返回错误描述。
        /// </summary>
        public string? SendCommandViaRest(string cmd, out string output)
        {
            output = "";
            ArmWatchdogSuppression(cmd);
            var manifest = _profile.LoadManifest();
            var port = manifest?.RestPort ?? 0;
            if (port <= 0) return "该服未配置 REST 端口（config.json 的 REST端口）";
            var token = ReadRestToken();
            if (string.IsNullOrEmpty(token)) return "该服 tshock\\config.json 里没有 REST 令牌（Rest外部应用令牌字典）";

            string? lastErr = null;
            // v3 为准；/server/rawcmd 是 TShock 注册的重定向，作为兜底
            foreach (var path in new[] { "v3/server/rawcmd", "server/rawcmd" })
            {
                try
                {
                    var url = $"http://127.0.0.1:{port}/{path}?token={Uri.EscapeDataString(token)}&cmd={Uri.EscapeDataString(cmd)}";
                    using var content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");
                    using var resp = Http.PostAsync(url, content).GetAwaiter().GetResult();
                    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) { lastErr = $"HTTP {(int)resp.StatusCode}（{path}）"; continue; }

                    output = FormatRestResponse(body);
                    return null;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            return lastErr ?? "未知错误";
        }

        private static bool IsManualStopCommand(string? command)
        {
            var name = (command ?? "").Trim().TrimStart('/').Split(' ', 2)[0];
            return string.Equals(name, "stop", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "exit", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "off", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "stop-nosave", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "exit-nosave", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "off-nosave", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWatchdogMaintenanceCommand(string? command)
        {
            var name = (command ?? "").Trim().TrimStart('/').Split(' ', 2)[0];
            return string.Equals(name, "hr", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "reload", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "world", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "save", StringComparison.OrdinalIgnoreCase);
        }

        private void ArmWatchdogSuppression(string? command)
        {
            if (IsManualStopCommand(command))
            {
                _userStopRequested = true;
                _watchdogSuppressUntilUtc = DateTime.UtcNow.AddSeconds(120);
                return;
            }

            if (!IsWatchdogMaintenanceCommand(command)) return;
            _healthFailureCount = 0;
            _lastHealthyUtc = DateTime.UtcNow;
            var seconds = Math.Max(120, _managerConfig.watchdogMaintenanceSuppressSeconds);
            _watchdogSuppressUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
            AddText($"[看门狗] 检测到维护命令，暂停健康检查 {seconds} 秒\n");
        }

        /// <summary>读取该服 TShock REST JSON 接口。成功返回 null，body 为响应文本。</summary>
        public string? GetRestJson(string relativePath, out string body)
        {
            body = "";
            var manifest = _profile.LoadManifest();
            var port = manifest?.RestPort ?? 0;
            if (port <= 0) return "该服未配置 REST 端口";
            var token = ReadRestToken();
            if (string.IsNullOrEmpty(token)) return "该服没有 REST 令牌";
            try
            {
                var path = (relativePath ?? "").TrimStart('/');
                var separator = path.Contains('?') ? "&" : "?";
                var url = $"http://127.0.0.1:{port}/{path}{separator}token={Uri.EscapeDataString(token)}";
                using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
                body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>REST 返回 JSON 里的 response 可能是字符串或字符串数组，统一成可读文本。</summary>
        private static string FormatRestResponse(string body)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<dynamic>(body);
                string text;
                var resp = obj?.response;
                if (resp == null) text = body;
                else if (resp is Newtonsoft.Json.Linq.JArray arr)
                    text = string.Join("\n", arr.Select(x => x?.ToString() ?? ""));
                else text = resp.ToString();

                // ★ 保留 Terraria 的 [c/RRGGBB:文字] 颜色标记：交给面板渲染成真实颜色（/help 就是靠这个上色）
                return text.Replace("\r", "").TrimEnd();
            }
            catch { return body; }
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
                foreach (var pr in ((Newtonsoft.Json.Linq.JObject)dict).Properties()) return pr.Name;
            }
            catch { }
            return null;
        }

        // ---------- 彩色文本 ----------
        /// <summary>
        /// 控制台着色规则：
        ///   TShock 标准输出=金色  Server API/提示=青  插件标签=紫
        ///   错误=红  警告=琥珀  成功/状态=绿  管理器/指令=蓝  列表/次要=灰
        /// </summary>
        private static Brush ColorFor(string line)
        {
            var s = line.TrimStart();
            if (s.Length == 0) return ClrNormal;

            // 管理器发出的指令回显
            if (s.StartsWith(">")) return ClrCmd;

            // 真错误（用具体特征，避免 ExceptionProbe / HotReload 这类名字被误判）
            if (s.Contains("Exception:") || s.Contains("Unhandled exception") ||
                s.Contains("StackOverflow", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Startup aborted", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Failed to load", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("致命") || s.Contains("错误") || s.Contains("失败") ||
                s.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Fatal", StringComparison.OrdinalIgnoreCase)) return ClrError;

            // 警告 / 缺失
            if (s.Contains("警告") || s.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("已跳过") || s.Contains("找不到") || s.Contains("未找到") ||
                s.Contains("不存在") || s.Contains("超时") ||
                s.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("missing", StringComparison.OrdinalIgnoreCase)) return ClrWarn;

            // 关键成功状态
            if (s.Contains("服务器已启动") || s.Contains("正在侦听") || s.Contains("插件同步") ||
                s.Contains("正在监听") || s.Contains("[启动]") || s.Contains("总库齐备") || s.Contains("已加载") ||
                s.Contains("已启用") || s.Contains("已初始化") || s.Contains("已注册") ||
                s.Contains("已创建") || s.Contains("已开启") || s.Contains("已刷新") ||
                s.Contains("已更新") || s.Contains("已就绪") || s.Contains("全部完成") ||
                s.Contains("同步完成")) return ClrGood;

            // 管理器状态标签：失败/超时已在上面拦截，剩余状态统一蓝色。
            if (s.StartsWith("[看门狗]") || s.StartsWith("[告警]")) return ClrWarn;
            if (s.StartsWith("[插件同步]") || s.StartsWith("[插件开关]")) return ClrPlugin;
            if (s.StartsWith("[顺序启动]") ||
                s.StartsWith("[刷新]") || s.StartsWith("[服务器设置]") ||
                s.StartsWith("[新建服务器]") || s.StartsWith("[回滚中心]") ||
                s.StartsWith("[计划任务]") || s.StartsWith("[玩家管理]") ||
                s.StartsWith("[快捷指令]") || s.StartsWith("[全部启动]") ||
                s.StartsWith("[停止]")) return ClrCmd;

            // 提示 / 说明
            if (s.StartsWith(":") || s.Contains("输入“help”") || s.Contains("输入\"help\"") ||
                s.Contains("DisableUUIDLogin") || s.Contains("UUID")) return ClrInfo;

            // 玩家、连接、世界等运行信息
            if (s.Contains("已加入") || s.Contains("已离开") || s.Contains("加入游戏") ||
                s.Contains("离开游戏") || s.Contains("登录") || s.Contains("连接") ||
                s.Contains("世界") || s.Contains("保存") || s.Contains("备份")) return ClrInfo;

            // TShock Server API 信息：青色，区别于普通金色 TShock 输出。
            if (s.StartsWith("[Server API]")) return ClrInfo;

            // 插件/功能标签：紫色
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

        private void AddText(string text, bool isError = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var dispatcher = _para.Dispatcher;
            if (dispatcher == null) return;
            try { dispatcher.BeginInvoke(new Action(() => AppendText(text, isError))); } catch { }
        }

        /// <summary>Terraria 彩色文本标记：[c/RRGGBB:文字]</summary>
        private static readonly System.Text.RegularExpressions.Regex ColorTagRx =
            new(@"\[c/([0-9A-Fa-f]{6}):(.*?)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>控制台 ANSI 颜色标记：\x1b[38;2;R;G;Bm / \x1b[0m</summary>
        private static readonly System.Text.RegularExpressions.Regex AnsiColorRx =
            new(@"\x1B\[([0-9;]*)m", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly Dictionary<string, Brush> ColorBrushCache = new();

        private static Brush BrushOfRgb(string hex, Brush fallback)
        {
            lock (ColorBrushCache)
            {
                if (ColorBrushCache.TryGetValue(hex, out var cached)) return cached;
                try
                {
                    var r = Convert.ToByte(hex.Substring(0, 2), 16);
                    var g = Convert.ToByte(hex.Substring(2, 2), 16);
                    var b = Convert.ToByte(hex.Substring(4, 2), 16);
                    var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                    brush.Freeze();
                    ColorBrushCache[hex] = brush;
                    return brush;
                }
                catch { return fallback; }
            }
        }

        /// <summary>把一行渲染进面板：带 [c/RRGGBB:...] 的按真实颜色拆成多段，否则按关键字着色。</summary>
        private void AppendLine(string raw, bool isError = false)
        {
            if (AnsiColorRx.IsMatch(raw))
            {
                AppendAnsiLine(raw, isError);
                return;
            }

            if (!ColorTagRx.IsMatch(raw))
            {
                _para.Inlines.Add(new Run(raw + "\n") { Foreground = isError ? ClrError : ColorFor(raw) });
                return;
            }

            var fallback = isError ? ClrError : ColorFor(raw);
            var idx = 0;
            foreach (System.Text.RegularExpressions.Match m in ColorTagRx.Matches(raw))
            {
                if (m.Index > idx)
                    _para.Inlines.Add(new Run(raw.Substring(idx, m.Index - idx)) { Foreground = fallback });
                _para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = BrushOfRgb(m.Groups[1].Value, fallback) });
                idx = m.Index + m.Length;
            }
            if (idx < raw.Length)
                _para.Inlines.Add(new Run(raw.Substring(idx)) { Foreground = fallback });
            _para.Inlines.Add(new Run("\n") { Foreground = fallback });
        }

        /// <summary>解析插件写出的 ANSI 真彩色，避免 TSM 控制台直接显示 [38;2;...m 这种乱码。</summary>
        private void AppendAnsiLine(string raw, bool isError)
        {
            var plain = AnsiColorRx.Replace(raw, "");
            var fallback = isError ? ClrError : ColorFor(plain);
            var current = fallback;
            var idx = 0;

            foreach (System.Text.RegularExpressions.Match m in AnsiColorRx.Matches(raw))
            {
                if (m.Index > idx)
                {
                    _para.Inlines.Add(new Run(raw.Substring(idx, m.Index - idx)) { Foreground = current });
                }

                current = BrushOfAnsiCode(m.Groups[1].Value, fallback);
                idx = m.Index + m.Length;
            }

            if (idx < raw.Length)
            {
                _para.Inlines.Add(new Run(raw.Substring(idx)) { Foreground = current });
            }

            _para.Inlines.Add(new Run("\n") { Foreground = current });
        }

        private static Brush BrushOfAnsiCode(string code, Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(code) || code == "0") return fallback;

            var parts = code.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts[0] == "0")
            {
                parts = parts.Skip(1).ToArray();
            }

            if (parts.Length >= 5 && parts[0] == "38" && parts[1] == "2")
            {
                try
                {
                    var r = Convert.ToByte(parts[2]);
                    var g = Convert.ToByte(parts[3]);
                    var b = Convert.ToByte(parts[4]);
                    var hex = $"{r:X2}{g:X2}{b:X2}";
                    return BrushOfRgb(hex, fallback);
                }
                catch { return fallback; }
            }

            var last = parts.LastOrDefault() ?? "";
            return last switch
            {
                "39" => fallback,
                "30" => BrushOfRgb("000000", fallback),
                "31" => BrushOfRgb("C00000", fallback),
                "32" => BrushOfRgb("00A000", fallback),
                "33" => BrushOfRgb("C0A000", fallback),
                "34" => BrushOfRgb("0000C0", fallback),
                "35" => BrushOfRgb("A000A0", fallback),
                "36" => BrushOfRgb("00A0A0", fallback),
                "37" => BrushOfRgb("C0C0C0", fallback),
                "90" => BrushOfRgb("808080", fallback),
                "91" => BrushOfRgb("FF4040", fallback),
                "92" => BrushOfRgb("40FF40", fallback),
                "93" => BrushOfRgb("FFFF40", fallback),
                "94" => BrushOfRgb("4090FF", fallback),
                "95" => BrushOfRgb("FF40FF", fallback),
                "96" => BrushOfRgb("40FFFF", fallback),
                "97" => BrushOfRgb("FFFFFF", fallback),
                _ => fallback
            };
        }

        private void AppendText(string text, bool isError = false)
        {
            try
            {
                foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (raw.Length == 0) continue;
                    AppendLine(raw, isError);
                    if (IsWatchdogManualStopLine(raw))
                    {
                        _userStopRequested = true;
                        _watchdogSuppressUntilUtc = DateTime.UtcNow.AddSeconds(120);
                    }
                    if (IsWatchdogMaintenanceLine(raw))
                    {
                        _healthFailureCount = 0;
                        _lastHealthyUtc = DateTime.UtcNow;
                        var seconds = Math.Max(120, _managerConfig.watchdogMaintenanceSuppressSeconds);
                        _watchdogSuppressUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
                    }
                    if (IsSevereLine(raw)) RaiseAlert($"服务器日志出现严重异常：{Name}", raw.Trim());
                    RecordWatchdogLogError(raw);
                }
                WriteToFile(text);          // ★ 同时落盘，方便事后查（Logs\<服>-日期.log）

                while (_para.Inlines.Count > 4096)
                {
                    if (_para.Inlines.FirstInline == null) break;
                    _para.Inlines.Remove(_para.Inlines.FirstInline);
                }
                OnTextChanged?.Invoke(this);
            }
            catch { }
        }

        private static bool IsWatchdogMaintenanceLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(
                       line,
                       @"执行(?:了)?\s*(?:指令|命令)?\s*/(?:hr|reload|world|save)\b",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                   line.Contains("热重载", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("重载世界", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("热重置地图", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("reloading world", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("重新加载世界", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWatchdogManualStopLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(
                line,
                @"执行(?:了)?\s*(?:指令|命令)?\s*/(?:stop|exit|off)(?:-nosave)?\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 看门狗重启前清理同一运行沙箱遗留的 TShock 进程，避免 ServerLog.txt / 世界文件句柄
        /// 还没释放就启动新进程，导致“重启后立刻启动失败”的二次故障。
        /// 只匹配当前沙箱路径，不会误杀其他服务器。
        /// </summary>
        private void KillResidualServerProcess(string executable)
        {
            try
            {
                var wanted = Path.GetFullPath(executable);
                var processName = Path.GetFileNameWithoutExtension(wanted);
                var killed = false;

                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId) continue;
                        var actual = TryGetProcessExecutable(process);
                        if (actual == null) continue;
                        if (!string.Equals(Path.GetFullPath(actual), wanted, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!process.HasExited)
                        {
                            AddText($"[看门狗] 发现同沙箱残留进程 PID {process.Id}，正在清理\n");
                            try { process.Kill(true); }
                            catch { try { process.Kill(); } catch { } }
                            if (!process.WaitForExit(10000))
                            {
                                try { process.Kill(); } catch { }
                                process.WaitForExit(5000);
                            }
                            killed = true;
                        }
                    }
                    catch { }
                    finally { try { process.Dispose(); } catch { } }
                }

                if (killed)
                {
                    AddText("[看门狗] 已清理残留服务端，等待文件句柄释放\n");
                    System.Threading.Thread.Sleep(2000);
                }
            }
            catch { }
        }

        private static string? TryGetProcessExecutable(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
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
