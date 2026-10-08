using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PGameTSManager;

internal sealed class ExternalProcessRuntime
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ExternalProcessProfile _profile;
    private Process? _process;

    public ExternalProcessRuntime(ExternalProcessProfile profile)
    {
        _profile = profile;
    }

    public string Id => _profile.id;
    public string Name => string.IsNullOrWhiteSpace(_profile.name) ? _profile.id : _profile.name;

    public object Snapshot(bool deep = false)
    {
        var process = FindProcess();
        var logActive = LogActive();
        var http = _profile.healthMode.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? ProbeHttp()
            : (bool?)null;
        var running = _profile.healthMode.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? process != null || http == true
            : _profile.healthMode.Equals("process-log", StringComparison.OrdinalIgnoreCase)
                ? process != null || logActive
                : process != null;
        return new
        {
            id = _profile.id,
            name = Name,
            enabled = _profile.enabled,
            kind = _profile.kind,
            state = running ? "running" : "stopped",
            pid = process?.Id,
            processName = _profile.processName,
            healthMode = _profile.healthMode,
            healthOk = http,
            healthUrl = _profile.healthUrl,
            healthPorts = _profile.healthPorts,
            logDir = _profile.logDir,
            logActive,
            watchdog = _profile.watchdog,
            autoRestart = _profile.autoRestart,
            at = DateTimeOffset.Now.ToString("o")
        };
    }

    public async Task StartAsync()
    {
        if (!_profile.enabled) throw new InvalidOperationException("external process disabled");
        if (FindProcess() != null) return;
        var workdir = Resolve(_profile.workdir);
        var exe = Resolve(_profile.exe, workdir);
        if (!File.Exists(exe)) throw new FileNotFoundException("external process executable not found", exe);

        var info = new ProcessStartInfo
        {
            WorkingDirectory = workdir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "cmd.exe";
            info.Arguments = "/c " + Quote(exe) + JoinArgs(_profile.args);
        }
        else
        {
            info.FileName = exe;
            info.Arguments = JoinArgs(_profile.args);
        }

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => WriteLog(e.Data);
        _process.ErrorDataReceived += (_, e) => WriteLog(e.Data);
        _process.Start();
        try { _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); } catch { }
        ControlEventHub.Publish("process.started", null, new { processId = _profile.id, pid = SafePid(_process) });
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_profile.stopArgs.Count > 0)
        {
            var workdir = Resolve(_profile.workdir);
            var exe = Resolve(_profile.exe, workdir);
            var stop = new ProcessStartInfo
            {
                FileName = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                           exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? "cmd.exe" : exe,
                Arguments = (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                             exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                    ? "/c " + Quote(exe) + JoinArgs(_profile.stopArgs)
                    : JoinArgs(_profile.stopArgs),
                WorkingDirectory = workdir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var stopProcess = Process.Start(stop);
            if (stopProcess != null)
            {
                await stopProcess.WaitForExitAsync();
                if (stopProcess.ExitCode != 0)
                    throw new InvalidOperationException("external stop command exit code " + stopProcess.ExitCode);
            }
        }

        var process = FindProcess();
        if (process != null)
        {
            try { process.Kill(true); }
            catch { try { process.Kill(); } catch { } }
            try { await process.WaitForExitAsync(); } catch { }
        }
        ControlEventHub.Publish("process.stopped", null, new { processId = _profile.id });
    }

    private Process? FindProcess()
    {
        try
        {
            if (_process != null && !_process.HasExited) return _process;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(_profile.processName)) return null;
        try
        {
            return Process.GetProcessesByName(_profile.processName)
                .OrderByDescending(p => SafeStartTime(p))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private bool LogActive()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_profile.logDir) || !Directory.Exists(_profile.logDir)) return false;
            var latest = Directory.GetFiles(_profile.logDir, "*.log", SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            return (DateTime.UtcNow - latest).TotalSeconds <= Math.Max(30, _profile.logActiveSeconds);
        }
        catch { return false; }
    }

    private bool ProbeHttp()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_profile.healthUrl)) return false;
            using var resp = Http.GetAsync(_profile.healthUrl).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private void WriteLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            var dir = string.IsNullOrWhiteSpace(_profile.logDir)
                ? ManagerConfig.Resolve("Core\\Logs")
                : Resolve(_profile.logDir);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "external-" + _profile.id + "-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            lock (this) File.AppendAllText(file, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }

    private static string Resolve(string path, string? baseDir = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return baseDir ?? ManagerConfig.BaseDir;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(baseDir ?? ManagerConfig.BaseDir, path));
    }

    private static string JoinArgs(IEnumerable<string> args)
        => args.Count() == 0 ? "" : " " + string.Join(" ", args.Select(Quote));

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static DateTime SafeStartTime(Process process)
    {
        try { return process.StartTime; } catch { return DateTime.MinValue; }
    }
    private static int? SafePid(Process? process)
    {
        try { return process?.Id; } catch { return null; }
    }
}
