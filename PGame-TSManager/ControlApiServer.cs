using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PGameTSManager;

internal sealed class ControlApiServer : IDisposable
{
    private readonly ManagerConfig _config;
    private readonly Func<MainWindow?> _windowFactory;
    private readonly CancellationTokenSource _cts = new();
    private HttpListener? _listener;
    private Task? _loop;
    private readonly object _sseGate = new();
    private readonly Dictionary<string, ExternalProcessRuntime> _externalProcesses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SSE 注释心跳间隔（秒）：避免客户端长时间收不到任何字节而误判断线、反复重连。</summary>
    private const int SseHeartbeatSeconds = 25;

    /// <summary>本管理器进程的启动时间（客户端用来识别 TSM 是否重启过）。</summary>
    private static readonly DateTimeOffset ManagerStartedAt = ResolveProcessStart();

    private static DateTimeOffset ResolveProcessStart()
    {
        try { return new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime); }
        catch { return DateTimeOffset.Now; }
    }

    public ControlApiServer(ManagerConfig config, Func<MainWindow?> windowFactory)
    {
        _config = config;
        _windowFactory = windowFactory;
    }

    public void Start()
    {
        if (!_config.controlApiEnabled) return;
        ControlEventHub.BufferSize = Math.Max(32, _config.controlApiEventBuffer);
        _externalProcesses.Clear();
        foreach (var process in _config.externalProcesses.Where(x => x != null && x.enabled && !string.IsNullOrWhiteSpace(x.id)))
            _externalProcesses[process.id] = new ExternalProcessRuntime(process);
        if (string.IsNullOrWhiteSpace(_config.controlApiToken))
        {
            _config.controlApiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            _config.Save();
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{_config.controlApiHost}:{_config.controlApiPort}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        try { _loop?.Wait(3000); } catch { }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener!.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!Authorized(ctx.Request))
            {
                await WriteJsonAsync(ctx, 401, new { ok = false, error = "unauthorized" });
                return;
            }

            var path = (ctx.Request.Url?.AbsolutePath ?? "").TrimEnd('/');
            var method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && path == "/tsm/status")
            {
                await HandleStatusAsync(ctx);
                return;
            }
            if (method == "GET" && path == "/tsm/servers")
            {
                await HandleServersAsync(ctx);
                return;
            }
            if (method == "GET" && path == "/tsm/events")
            {
                await HandleEventsAsync(ctx);
                return;
            }
            if (method == "GET" && path == "/tsm/external")
            {
                await WriteJsonAsync(ctx, 200, new
                {
                    ok = true,
                    contractVersion = "tsm.control.v1",
                    externalProcesses = _externalProcesses.Values.Select(x => x.Snapshot(true)).ToList()
                });
                return;
            }
            if (method == "GET" && path.StartsWith("/tsm/external/", StringComparison.OrdinalIgnoreCase))
            {
                var id = Uri.UnescapeDataString(path["/tsm/external/".Length..]);
                if (!_externalProcesses.TryGetValue(id, out var external))
                {
                    await WriteJsonAsync(ctx, 404, new { ok = false, error = "external_process_not_found", id });
                    return;
                }
                await WriteJsonAsync(ctx, 200, new { ok = true, contractVersion = "tsm.control.v1", process = external.Snapshot(true) });
                return;
            }
            if (method == "POST" && path == "/tsm/world/rebuild")
            {
                await HandleWorldRebuildAsync(ctx);
                return;
            }
            if (method == "POST" && path.StartsWith("/tsm/external/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleExternalActionAsync(ctx, path);
                return;
            }
            if (method == "POST" && path.StartsWith("/tsm/servers/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleServerActionAsync(ctx, path);
                return;
            }

            await WriteJsonAsync(ctx, 404, new { ok = false, error = "not_found" });
        }
        catch (Exception ex)
        {
            try { await WriteJsonAsync(ctx, 500, new { ok = false, error = ex.Message }); } catch { }
        }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_config.controlApiToken)) return false;
        var token = request.Headers["X-TSM-Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            var auth = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }
        return string.Equals(token, _config.controlApiToken, StringComparison.Ordinal);
    }

    private async Task HandleStatusAsync(HttpListenerContext ctx)
    {
        var window = _windowFactory();
        if (window == null)
        {
            await WriteJsonAsync(ctx, 503, new { ok = false, error = "manager_not_ready" });
            return;
        }

        var data = await window.Dispatcher.InvokeAsync(() =>
        {
            var running = window.Containers.Count(c => c.IsRunning);
            return new
            {
                ok = true,
                contractVersion = "tsm.control.v1",
                managerVersion = typeof(ControlApiServer).Assembly.GetName().Version?.ToString() ?? "1.1.0",
                managerRunning = true,
                serverCount = window.Containers.Count,
                runningCount = running,
                externalCount = _externalProcesses.Count,
                managerStartedAt = ManagerStartedAt.ToString("o"),
                eventEpoch = ControlEventHub.EventEpoch,
                at = DateTimeOffset.Now.ToString("o")
            };
        }).Task;
        await WriteJsonAsync(ctx, 200, data);
    }

    private async Task HandleServersAsync(HttpListenerContext ctx)
    {
        var window = _windowFactory();
        if (window == null)
        {
            await WriteJsonAsync(ctx, 503, new { ok = false, error = "manager_not_ready" });
            return;
        }

        var data = await window.Dispatcher.InvokeAsync(() =>
            window.Containers.Select(c => new
            {
                id = c.StableId,
                name = c.Name,
                label = c.ListLabel,
                state = c.IsRunning ? "running" : "stopped",
                gamePort = c.GamePort,
                restPort = c.RestPort,
                restTokenAlias = c.StableId + "-rest",
                world = c.WorldName,
                pid = c.SafeProcessId(),
                rebuilding = c.IsRebuilding,
                at = DateTimeOffset.Now.ToString("o")
            }).ToList()
        ).Task;
        await WriteJsonAsync(ctx, 200, new { ok = true, contractVersion = "tsm.control.v1", servers = data });
    }

    private async Task HandleServerActionAsync(HttpListenerContext ctx, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) { await WriteJsonAsync(ctx, 400, new { ok = false, error = "invalid_path" }); return; }
        var id = Uri.UnescapeDataString(parts[2]);
        var action = string.Join("/", parts.Skip(3)).ToLowerInvariant();
        var window = _windowFactory();
        if (window == null) { await WriteJsonAsync(ctx, 503, new { ok = false, error = "manager_not_ready" }); return; }

        var container = await window.Dispatcher.InvokeAsync(() => window.FindContainer(id)).Task;
        if (container == null) { await WriteJsonAsync(ctx, 404, new { ok = false, error = "server_not_found", id }); return; }

        if (action == "command")
        {
            var body = await ReadBodyAsync(ctx.Request);
            var command = JObject.Parse(body)["command"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(command))
            {
                await WriteJsonAsync(ctx, 400, new { ok = false, error = "command_required" });
                return;
            }
            var err = container.SendCommandViaRest(command, out var output);
            await WriteJsonAsync(ctx, err == null ? 200 : 400, new { ok = err == null, command, output, error = err });
            return;
        }

        if (action == "start")
        {
            await window.Dispatcher.InvokeAsync(() => { if (!container.IsRunning) container.IsRunning = true; }).Task;
            await WriteJsonAsync(ctx, 202, new { ok = true, id, state = "starting" });
            return;
        }
        if (action == "stop")
        {
            await window.Dispatcher.InvokeAsync(() => { if (container.IsRunning) container.IsRunning = false; }).Task;
            await WriteJsonAsync(ctx, 202, new { ok = true, id, state = "stopping" });
            return;
        }
        if (action == "restart")
        {
            _ = Task.Run(async () =>
            {
                await window.Dispatcher.InvokeAsync(() => { if (container.IsRunning) container.IsRunning = false; });
                await container.WaitForStoppedAsync(TimeSpan.FromSeconds(30));
                await window.Dispatcher.InvokeAsync(() => { if (!container.IsRunning) container.IsRunning = true; });
            });
            await WriteJsonAsync(ctx, 202, new { ok = true, id, state = "restarting" });
            return;
        }
        if (action == "syncplugins")
        {
            // 把插件总库同步进运行沙箱（文件不被锁）；调用方随后可 /hr load 热升级。
            try
            {
                var summary = await window.Dispatcher.InvokeAsync(() => container.SyncPluginsNow()).Task;
                await WriteJsonAsync(ctx, 200, new { ok = true, id, summary });
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(ctx, 500, new { ok = false, id, error = ex.Message });
            }
            return;
        }
        if (action == "plugins/reload")
        {
            // 结构化插件热更：先确认运行沙箱里存在该 DLL，再发 /hr load 并回传结果。
            // 与 /command 不同：插件不存在时明确回 404 plugin_not_found。
            var body = await ReadBodyAsync(ctx.Request);
            var raw = "";
            try { raw = JObject.Parse(body)["plugin"]?.ToString() ?? ""; } catch { }
            var plugin = raw.Trim();
            if (string.IsNullOrWhiteSpace(plugin))
            {
                await WriteJsonAsync(ctx, 400, new { ok = false, error = "plugin_required" });
                return;
            }
            var name = plugin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? plugin[..^4] : plugin;
            var dll = Path.Combine(container.ServerPluginsDirectory, name + ".dll");
            if (!File.Exists(dll))
            {
                await WriteJsonAsync(ctx, 404, new { ok = false, error = "plugin_not_found", plugin = name });
                return;
            }
            var err = container.SendCommandViaRest("/hr load " + name, out var output);
            await WriteJsonAsync(ctx, err == null ? 200 : 400,
                new { ok = err == null, plugin = name, found = true, output, error = err });
            return;
        }

        await WriteJsonAsync(ctx, 404, new { ok = false, error = "unknown_action" });
    }

    private async Task HandleWorldRebuildAsync(HttpListenerContext ctx)
    {
        var body = await ReadBodyAsync(ctx.Request);
        var json = JObject.Parse(body);
        var id = json["serverId"]?.ToString() ?? "";
        var requestId = json["requestId"]?.ToString() ?? "";
        var reason = json["reason"]?.ToString() ?? "";
        var actor = json["actor"]?.ToString() ?? "";
        var confirm = json["confirm"]?.ToObject<bool>() ?? false;
        var dryRun = json["dryRun"]?.ToObject<bool>() ?? false;
        if (dryRun && string.IsNullOrWhiteSpace(requestId)) requestId = "dryrun-" + Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(requestId) || (!dryRun && !confirm))
        {
            await WriteJsonAsync(ctx, 400, new { ok = false, error = "serverId_requestId_confirm_required" });
            return;
        }

        var window = _windowFactory();
        if (window == null) { await WriteJsonAsync(ctx, 503, new { ok = false, error = "manager_not_ready" }); return; }
        var container = await window.Dispatcher.InvokeAsync(() => window.FindContainer(id)).Task;
        if (container == null) { await WriteJsonAsync(ctx, 404, new { ok = false, error = "server_not_found", id }); return; }

        if (dryRun)
        {
            try
            {
                var preview = await window.Dispatcher.InvokeAsync(() => container.PreviewRebuild(requestId, actor)).Task;
                await WriteJsonAsync(ctx, 200, new
                {
                    ok = true,
                    contractVersion = "tsm.control.v1",
                    dryRun = true,
                    serverId = id,
                    requestId,
                    worldPath = preview.WorldPath,
                    worldName = preview.WorldName,
                    worldExists = preview.WorldExists,
                    worldSize = preview.WorldSize,
                    wouldBackupTo = preview.WouldBackupTo,
                    autoCreate = preview.AutoCreate,
                    difficulty = preview.Difficulty,
                    worldevil = preview.WorldEvil,
                    seed = string.IsNullOrWhiteSpace(preview.Seed) ? "<random>" : preview.Seed,
                    serverPropertiesExists = preview.ServerPropertiesExists,
                    note = "dryRun 不产生任何文件/进程变更"
                });
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(ctx, 400, new { ok = false, error = ex.Message });
            }
            return;
        }

        if (!container.TryBeginRebuild())
        {
            await WriteJsonAsync(ctx, 409, new { ok = false, error = "rebuild_in_progress", serverId = id, requestId });
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var op = window.Dispatcher.InvokeAsync(() => container.RebuildWorldAsync(requestId, actor));
                await op.Task.Unwrap();
            }
            catch (Exception ex)
            {
                ControlEventHub.Publish("world.rebuild.failed", container.StableId,
                    new { requestId, actor, reason, error = ex.Message });
            }
            finally
            {
                container.EndRebuild();
            }
        });

        await WriteJsonAsync(ctx, 202, new
        {
            ok = true,
            contractVersion = "tsm.control.v1",
            serverId = id,
            requestId,
            state = "accepted"
        });
    }

    private async Task HandleExternalActionAsync(HttpListenerContext ctx, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) { await WriteJsonAsync(ctx, 400, new { ok = false, error = "invalid_path" }); return; }
        var id = Uri.UnescapeDataString(parts[2]);
        var action = parts[3].ToLowerInvariant();
        if (!_externalProcesses.TryGetValue(id, out var process))
        {
            await WriteJsonAsync(ctx, 404, new { ok = false, error = "external_process_not_found", id });
            return;
        }

        try
        {
            if (action == "start") await process.StartAsync();
            else if (action == "stop") await process.StopAsync();
            else if (action == "restart")
            {
                await process.StopAsync();
                await process.StartAsync();
            }
            else
            {
                await WriteJsonAsync(ctx, 404, new { ok = false, error = "unknown_action" });
                return;
            }
            await WriteJsonAsync(ctx, 202, new { ok = true, id, state = action == "stop" ? "stopping" : "starting" });
        }
        catch (Exception ex)
        {
            ControlEventHub.Publish("process.unhealthy", null, new { processId = id, error = ex.Message });
            await WriteJsonAsync(ctx, 500, new { ok = false, error = ex.Message });
        }
    }

    private async Task HandleEventsAsync(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.SendChunked = true;

        var lastId = ParseLastEventId(ctx.Request, out var hasExplicitLastId);
        // 没有 Last-Event-ID/since 时，默认只接收连接后的新事件，避免首次连接灌入整段历史。
        if (!hasExplicitLastId) lastId = ControlEventHub.LatestSeq;
        WriteSseComment(response, ": connected\n\n");
        foreach (var ev in ControlEventHub.After(lastId))
            WriteSse(response, ev);

        using var sub = ControlEventHub.Subscribe(ev => WriteSse(response, ev));
        try
        {
            // 周期写 SSE 注释 ": ping"，既是保活心跳，也用作断连探测：
            // 写失败（客户端已断开）即结束本连接处理，避免订阅与任务长期泄漏。
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(SseHeartbeatSeconds), _cts.Token);
                if (!WriteSseComment(response, ": ping\n\n")) break;
            }
        }
        catch { }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private static long ParseLastEventId(HttpListenerRequest request, out bool explicitValue)
    {
        explicitValue = false;
        var raw = request.Headers["Last-Event-ID"];
        if (!string.IsNullOrWhiteSpace(raw)) explicitValue = true;
        if (string.IsNullOrWhiteSpace(raw)) raw = request.QueryString["since"];
        if (!string.IsNullOrWhiteSpace(raw)) explicitValue = true;
        if (string.IsNullOrWhiteSpace(raw)) raw = request.QueryString["lastEventId"];
        if (!string.IsNullOrWhiteSpace(raw)) explicitValue = true;
        raw ??= "0";
        return long.TryParse(raw, out var value) ? value : 0;
    }

    private bool WriteSseComment(HttpListenerResponse response, string text)
    {
        try
        {
            lock (_sseGate)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Flush();
            }
            return true;
        }
        catch { return false; }
    }

    private void WriteSse(HttpListenerResponse response, ControlEvent ev)
    {
        try
        {
            lock (_sseGate)
            {
                var json = JsonConvert.SerializeObject(ev);
                var text = $"id: {ev.Seq}\nevent: {ev.Type}\ndata: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(text);
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Flush();
            }
        }
        catch { }
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int code, object body)
    {
        try
        {
            var json = JsonConvert.SerializeObject(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { }
    }

    public void Dispose() => Stop();
}
