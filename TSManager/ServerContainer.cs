using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using GameLauncher;
using Newtonsoft.Json;
using TSManager.Annotations;

namespace TSManager
{
    public class ServerContainer : INotifyPropertyChanged
    {
        private readonly string _serverName;
        private readonly string _configFile;
        private readonly ManagerConfig _managerConfig;
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
                    if (_process != null)
                    {
                        _process.Kill();
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

        public string Name => _serverName;

        public ServerContainer(ManagerConfig config, string serverName)
        {
            _configFile = Path.Combine(config.serverDir, serverName, config.configFile);
            if (!File.Exists(_configFile))
            {
                File.WriteAllText(_configFile, JsonConvert.SerializeObject(new ServerConfig(), Formatting.Indented));
            }

            _para = new Paragraph();
            _serverName = serverName;
            _managerConfig = config;
            Document = new FlowDocument(_para);
            Foreground = new SolidColorBrush(Colors.LightGray);
            Background = new SolidColorBrush(Colors.Black);
            _title = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;
        }

        private void Start()
        {
            _para.Inlines.Clear();
            var serverConfig = LoadServerConfig();
            var serverDirectory = Path.GetFullPath(Path.Combine(_managerConfig.serverDir, _serverName));
            Directory.CreateDirectory(serverDirectory);
            var executable = ResolveServerExecutable(serverDirectory);
            var info = new ProcessStartInfo
            {
                FileName = executable,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = serverDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8
            };

            if (_managerConfig.useTShockLaunchArguments)
            {
                var propertiesPath = Path.Combine(serverDirectory, _managerConfig.serverPropertiesFile);
                if (!File.Exists(propertiesPath))
                {
                    File.WriteAllText(propertiesPath, BuildTShockServerProperties(serverConfig, _managerConfig.worldDir), Encoding.UTF8);
                }

                info.ArgumentList.Add("-config");
                info.ArgumentList.Add(propertiesPath);
                AddWorldArgument(info, serverConfig, _managerConfig.worldDir);
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
                info.ArgumentList.Add(Path.GetFullPath(_configFile));
                info.ArgumentList.Add(Path.GetFullPath(_managerConfig.pluginDir));
                info.ArgumentList.Add(Path.GetFullPath(_managerConfig.worldDir));
                info.ArgumentList.Add(_serverName);
                info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }

            _process = new Process
            {
                StartInfo = info,
                EnableRaisingEvents = true
            };

            _process.Exited += (_, _) =>
            {
                AddText($"---process exited with code = {_process.ExitCode}---\n");
                _process.Dispose();
                _process = null;
                OnPropertyChanged(nameof(IsRunning));
            };

            _process.OutputDataReceived += (_, args) =>
            {
                AddText($"{args.Data}\n");
            };
            _process.Start();
            _process.StandardInput.AutoFlush = true;
            _process.BeginOutputReadLine();
            _process.StandardInput.WriteLine();

            OnPropertyChanged(nameof(IsRunning));
        }

        private ServerConfig LoadServerConfig()
        {
            try
            {
                return JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(_configFile)) ?? new ServerConfig();
            }
            catch
            {
                return new ServerConfig();
            }
        }

        private string ResolveServerExecutable(string serverDirectory)
        {
            var configured = _managerConfig.serverExecutable;
            var candidates = new[]
            {
                configured,
                Path.Combine(serverDirectory, configured),
                Path.Combine(AppContext.BaseDirectory, configured),
                Path.Combine(AppContext.BaseDirectory, "TShock.Server.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return configured;
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
                        _para.Inlines.Remove(_para.Inlines.FirstInline);
                OnTextChanged?.Invoke(this);
            });
        }

        public void SendText(string msg)
        {
            if (_process == null) throw new InvalidOperationException();
            AddText($"{msg}\n");
            _process.StandardInput.WriteLine(msg);
        }

        public override string ToString()
        {
            return _serverName;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
