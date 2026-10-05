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
using GameLauncher;
using Newtonsoft.Json;
using PGameTSManager.Annotations;

namespace PGameTSManager
{
    public class ServerContainer : INotifyPropertyChanged
    {
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
                    if (!IsRunning)
                    {
                        Start();
                        OnPropertyChanged(nameof(IsRunning));
                    }
                }
                else
                {
                    var process = _process;
                    if (process != null)
                    {
                        try { process.Kill(); }
                        catch { /* 进程已退出 */ }
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
            private set
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
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

            if (!IsProfileMode)
            {
                EnsureLegacyServerConfig();
            }
        }

        /// <summary>兼容旧版 Servers\&lt;名称&gt; 自建目录模式。</summary>
        public ServerContainer(ManagerConfig config, string serverName)
            : this(config, new ServerProfile { name = serverName })
        {
        }

        private bool IsProfileMode => _profile.IsProfileMode;

        private string ServerDirectory => IsProfileMode
            ? _profile.ResolvedRootPath
            : Path.GetFullPath(Path.Combine(ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name));

        private string LegacyConfigFile => Path.Combine(
            ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name, _managerConfig.configFile);

        private void Start()
        {
            try
            {
                StartCore();
            }
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
            var serverDirectory = ServerDirectory;
            Directory.CreateDirectory(serverDirectory);
            var executable = ResolveServerExecutable(serverDirectory);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException($"找不到服务端可执行文件：{executable}");
            }

            var showWindow = _managerConfig.showServerWindow;
            var info = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = serverDirectory,
                UseShellExecute = showWindow,
                CreateNoWindow = !showWindow
            };
            if (!showWindow)
            {
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardInputEncoding = Encoding.UTF8;
            }

            if (IsProfileMode)
            {
                // 映射模式：直接使用既有服务端目录，原样透传参数，不生成、不覆盖任何配置。
                if (_managerConfig.backupBeforeStart)
                {
                    BackupServerFiles(serverDirectory);
                }
                info.Arguments = PrepareProfileServer(serverDirectory);
            }
            else
            {
                BuildLegacyArguments(info, serverDirectory);
            }

            // 可见窗口模式（UseShellExecute=true）不支持 ArgumentList，转成字符串
            if (showWindow && info.ArgumentList.Count > 0)
            {
                info.Arguments = string.Join(" ", info.ArgumentList.Select(QuoteArgument));
                info.ArgumentList.Clear();
            }

            _process = new Process
            {
                StartInfo = info,
                EnableRaisingEvents = true
            };

            _process.Exited += (_, _) =>
            {
                AddText($"---process exited with code = {_process?.ExitCode}---\n");
                _process?.Dispose();
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
            };

            if (!showWindow)
            {
                _process.OutputDataReceived += (_, args) =>
                {
                    AddText($"{args.Data}\n");
                };
            }
            _process.Start();
            if (!showWindow)
            {
                _process.StandardInput.AutoFlush = true;
                _process.BeginOutputReadLine();
                _process.StandardInput.WriteLine();
            }

            OnPropertyChanged(nameof(IsRunning));
        }

        /// <summary>
        /// 映射模式启动前：读取每服 config.json（TSM 配置），按「总插件库」同步 ServerPlugins，
        /// 返回该服实际使用的启动参数。不生成、不覆盖任何服务器配置。
        /// </summary>
        private string PrepareProfileServer(string serverDirectory)
        {
            var manifest = _profile.LoadManifest();
            var arguments = !string.IsNullOrWhiteSpace(_profile.arguments)
                ? _profile.arguments
                : manifest?.Arguments ?? string.Empty;

            var pluginList = (_profile.plugins != null && _profile.plugins.Count > 0)
                ? _profile.plugins
                : manifest?.Plugins ?? new List<string>();
            var prune = manifest?.PrunePlugins ?? _managerConfig.prunePlugins;

            if (_managerConfig.syncPluginsOnStart && pluginList.Count > 0)
            {
                try
                {
                    var library = _managerConfig.ResolvePluginLibrary(_profile, manifest);
                    PluginSync.Apply(serverDirectory, library, pluginList, prune, _managerConfig.disabledPluginDir, AddText);
                }
                catch (Exception ex)
                {
                    AddText($"[插件同步] 失败：{ex.Message}\n");
                }
            }

            return arguments;
        }
        private void BuildLegacyArguments(ProcessStartInfo info, string serverDirectory)
        {
            var serverConfig = LoadServerConfig();
            var worldDir = ManagerConfig.Resolve(_managerConfig.worldDir);

            if (_managerConfig.useTShockLaunchArguments)
            {
                var propertiesPath = Path.Combine(serverDirectory, _managerConfig.serverPropertiesFile);
                if (!File.Exists(propertiesPath))
                {
                    File.WriteAllText(propertiesPath, BuildTShockServerProperties(serverConfig, worldDir), Encoding.UTF8);
                }

                info.ArgumentList.Add("-config");
                info.ArgumentList.Add(propertiesPath);
                AddWorldArgument(info, serverConfig, worldDir);
                info.ArgumentList.Add("-port");
                info.ArgumentList.Add(serverConfig.port.ToString(CultureInfo.InvariantCulture));
                info.ArgumentList.Add("-maxplayers");
                info.ArgumentList.Add(serverConfig.maxPlayer.ToString(CultureInfo.InvariantCulture));
                info.ArgumentList.Add("-lang");
                info.ArgumentList.Add(((int)serverConfig.lang).ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(serverConfig.password))
                {
                    info.ArgumentList.Add("-pass");
                    info.ArgumentList.Add(serverConfig.password);
                }
                foreach (var parameter in serverConfig.parameters)
                {
                    info.ArgumentList.Add(parameter);
                }
            }
            else
            {
                info.ArgumentList.Add(Path.GetFullPath(LegacyConfigFile));
                info.ArgumentList.Add(ManagerConfig.Resolve(_managerConfig.pluginDir));
                info.ArgumentList.Add(worldDir);
                info.ArgumentList.Add(_profile.name);
                info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void EnsureLegacyServerConfig()
        {
            try
            {
                var dir = Path.Combine(ManagerConfig.Resolve(_managerConfig.serverDir), _profile.name);
                Directory.CreateDirectory(dir);
                var configFile = LegacyConfigFile;
                if (!File.Exists(configFile))
                {
                    File.WriteAllText(configFile, JsonConvert.SerializeObject(new ServerConfig(), Formatting.Indented));
                }
            }
            catch
            {
                // 旧模式初始化失败不阻断界面加载。
            }
        }

        private ServerConfig LoadServerConfig()
        {
            try
            {
                return JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(LegacyConfigFile)) ?? new ServerConfig();
            }
            catch
            {
                return new ServerConfig();
            }
        }

        private string ResolveServerExecutable(string serverDirectory)
        {
            var configured = string.IsNullOrWhiteSpace(_profile.executable)
                ? _managerConfig.serverExecutable
                : _profile.executable;

            var sharedDir = ManagerConfig.Resolve(_managerConfig.sharedRuntimeDir);
            var candidates = _managerConfig.useSharedRuntime
                ? new[]
                {
                    Path.Combine(sharedDir, configured),
                    Path.Combine(serverDirectory, configured),
                    configured,
                    Path.Combine(AppContext.BaseDirectory, configured)
                }
                : new[]
                {
                    Path.Combine(serverDirectory, configured),
                    configured,
                    Path.Combine(sharedDir, configured),
                    Path.Combine(AppContext.BaseDirectory, configured)
                };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return Path.Combine(serverDirectory, configured);
        }

        /// <summary>把单个参数按 Windows 规则加引号（供 UseShellExecute 模式拼接命令行）。</summary>
        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            if (argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return argument;
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }
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
                    Path.Combine(serverDirectory, _managerConfig.serverPropertiesFile),
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

                if (copied)
                {
                    PruneBackups(profileBackupDir, _managerConfig.backupKeep);
                }
            }
            catch
            {
                // 备份失败不阻断服务器启动。
            }
        }

        private static void PruneBackups(string profileBackupDir, int keep)
        {
            if (keep <= 0 || !Directory.Exists(profileBackupDir)) return;
            var dirs = Directory.GetDirectories(profileBackupDir).OrderByDescending(d => d).Skip(keep);
            foreach (var dir in dirs)
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

        private static void AddWorldArgument(ProcessStartInfo info, ServerConfig config, string worldDirectory)
        {
            if (string.IsNullOrWhiteSpace(config.world))
            {
                return;
            }
            info.ArgumentList.Add("-world");
            info.ArgumentList.Add(Path.Combine(Path.GetFullPath(worldDirectory), config.world + ".wld"));
        }

        private static string BuildTShockServerProperties(ServerConfig config, string worldDirectory)
        {
            return string.Join(Environment.NewLine, new[]
            {
                "world=" + config.world,
                "worldpath=" + Path.GetFullPath(worldDirectory),
                "maxplayers=" + config.maxPlayer.ToString(CultureInfo.InvariantCulture),
                "port=" + config.port.ToString(CultureInfo.InvariantCulture),
                "password=" + config.password,
                "lang=" + ((int)config.lang).ToString(CultureInfo.InvariantCulture)
            });
        }

        private void AddText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _para.Dispatcher.Invoke(() =>
            {
                if (text[0] == 1)
                {
                    var args = text[6..^1];
                    switch (text[1..6])
                    {
                        case "title":
                            Title = args;
                            break;
                        case "fgclr":
                            Foreground = new SolidColorBrush((Color)(typeof(Colors).GetProperty(args)?.GetValue(null) ?? Colors.Gray));
                            break;
                        case "bgclr":
                            Background = new SolidColorBrush((Color)(typeof(Colors).GetProperty(args)?.GetValue(null) ?? Colors.Black));
                            break;
                    }
                    return;
                }
                _para.Inlines.Add(new Run(text)
                {
                    Background = Background,
                    Foreground = Foreground
                });
                if (_para.Inlines.Count > 2048)
                    for (var i = 0; i < 1024; ++i)
                    {
                        if (_para.Inlines.FirstInline == null) break;
                        _para.Inlines.Remove(_para.Inlines.FirstInline);
                    }
                OnTextChanged?.Invoke(this);
            });
        }

        public void SendText(string msg)
        {
            var process = _process;
            if (process == null) throw new InvalidOperationException("服务器未运行。");
            if (!process.StartInfo.RedirectStandardInput)
            {
                AddText("[提示] 当前是“可见窗口”模式，请直接在服务器窗口里输入指令。\n");
                return;
            }
            AddText($"{msg}\n");
            process.StandardInput.WriteLine(msg);
        }

        public override string ToString()
        {
            return Name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}