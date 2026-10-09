# PGame-TSManager Control API

> 状态：通用 Core 能力，默认关闭。
> 仅个人版 overlay 可以开启并配置 token；公共版默认不监听任何控制端口。

## 配置

```json
{
  "controlApiEnabled": true,
  "controlApiHost": "127.0.0.1",
  "controlApiPort": 8765,
  "controlApiToken": "",
  "controlApiEventBuffer": 512
}
```

`controlApiToken` 为空时由 TSM 首次启动生成并写回运行端 `config.json`。不要把 token 提交到 Git。

## 端点

```text
GET  /tsm/status
GET  /tsm/servers
POST /tsm/servers/{id}/start
POST /tsm/servers/{id}/stop
POST /tsm/servers/{id}/restart
POST /tsm/servers/{id}/command
POST /tsm/servers/{id}/syncplugins
POST /tsm/servers/{id}/plugins/reload
GET  /tsm/events
POST /tsm/world/rebuild
GET  /tsm/external
GET  /tsm/external/{id}
POST /tsm/external/{id}/start
POST /tsm/external/{id}/stop
POST /tsm/external/{id}/restart
```

鉴权：

```text
X-TSM-Token: <token>
```

或：

```text
Authorization: Bearer <token>
```

`id` 是每服稳定 `serverId`，不是中文名、目录名或端口。

## 事件

```json
{
  "type": "server.ready",
  "serverId": "server-a",
  "at": "2026-10-08T20:00:00+08:00",
  "seq": 1024,
  "payload": {}
}
```

`GET /tsm/events` 使用 SSE；客户端可传 `Last-Event-ID` 重放 ring buffer。

SSE 握手响应会先发送并 flush `: connected`。未带 `Last-Event-ID` 时默认只接收连接后的新事件；要在首次连接时回放历史，可显式传 `?since=0`（或 `?since=<seq>`）。

当前事件：

```text
server.state
server.ready
server.crash
server.restart
server.giveup
world.rebuild.started
world.rebuild.backup_created
world.rebuild.generating
world.rebuild.ready
world.rebuild.timeout
world.rebuild.failed
process.started
process.stopped
process.unhealthy
server.process.conflict
```

## World Rebuild

```json
{
  "serverId": "server-b",
  "requestId": "fixtools-20261008-001",
  "reason": "FixTools rebuild",
  "actor": "FixTools",
  "confirm": true,
  "backup": true,
  "startAfterRebuild": true
}
```

TSM 是 `server.properties` 和 `Servers\Worlds\*.wld` 的唯一写入者。

### 只读预演（dryRun）

带 `"dryRun": true` 时**只解析清单/世界路径/生成参数并回 200**，不停服、不备份、不删图、不建图；此模式**不需要 `confirm`**，`requestId` 缺省会自动生成。

```json
{ "serverId": "liuguang-realm", "dryRun": true }
```

```json
{
  "ok": true,
  "contractVersion": "tsm.control.v1",
  "dryRun": true,
  "serverId": "liuguang-realm",
  "requestId": "dryrun-<guid>",
  "worldPath": "Servers\\Worlds\\3.流光神域.wld",
  "worldName": "流光神域",
  "worldExists": true,
  "worldSize": 12404014,
  "wouldBackupTo": "...\\3.流光神域.wld.bak-<时间戳>",
  "autoCreate": 3,
  "difficulty": "2",
  "worldevil": "random",
  "seed": "392",
  "serverPropertiesExists": true,
  "note": "dryRun 不产生任何文件/进程变更"
}
```

同时会发布 `world.rebuild.dryrun` 事件。真建（`dryRun` 缺省/false）仍要求 `confirm:true` + `requestId`，否则 `400 serverId_requestId_confirm_required`。

并发保护：同一服的 rebuild 已在执行时，第二个请求返回 **`409 {ok:false, error:"rebuild_in_progress"}`**（不排队、不覆盖）。`GET /tsm/servers` 的每项带 `rebuilding:true/false`。

收尾事件 `world.rebuild.ready` / `world.rebuild.failed` 的 payload 带换图后的世界校验信息：

```json
{
  "Type": "world.rebuild.ready",
  "Payload": {
    "requestId": "…", "worldPath": "Servers\\Worlds\\2.泰拉大陆.wld",
    "ready": true,
    "size": 12317136,
    "mtimeUtc": "2026-10-08T16:04:50.1234567Z"
  }
}
```

> 校验建议：**同种子重建体积可能只差 1 字节**（确定性生成），所以请用「`ready:true` + `mtimeUtc` 变化」判定换图成功，不要只比 `size`。

### ⚠ 等待超时 ≠ 重建失败（`world.rebuild.timeout`）

大世界 + 全插件时，生成可能刚好超过首段等待窗口（= `max(300, watchdogWorldBuildSuppressSeconds + 120)` 秒；
配置 600 时即 720s）。实测：世界在超时后约 6 秒才落地 → 曾被误报 `world.rebuild.failed`。

现在改为：

1. 首段等待超时 **不再发 `failed`**，改发信息性事件 `world.rebuild.timeout`
   （payload：`requestId / worldPath / waitedSeconds / hint`）；
2. 随后**继续等待最多 15 分钟**，真正就绪才发 `world.rebuild.ready`；
3. `world.rebuild.failed` 现在只代表“确实没起来”（进程退出/始终未就绪）。

客户端建议：`timeout` 只提示「仍在进行」，**不要**当失败处理；最终以 `ready` / `failed` 为准。
另：`world.rebuild.generating` 的 `autoCreate/difficulty/worldevil/seed` 现在上报的是**实际生效**参数
（取自 `server.properties`，而不是清单里的 `自动建图`）。

## 插件热升级（Live Plugin Sync）

`POST /tsm/servers/{id}/syncplugins` 把插件总库按该服清单**同步进运行沙箱** `Core\_runtime\<服>\ServerPlugins`（不是根目录那份），运行中调用安全。

```json
{ "ok": true, "id": "liuguang-city", "summary": "新增 0 / 更新 1 / 停用 0，共 15 个启用插件" }
```

关键事实（远程实测 2026-10-08）：

- TShock 用**影子加载** `Assembly.Load(byte[], byte[])`（`TerrariaServer.dll::ServerApi.LoadPlugins`、`HotReload.Core::LoadPluginFromDisk`），**运行中的 ServerPlugins DLL 不会被锁**，可随时覆盖。
- `/hr load <插件>` **会重新读盘**：把沙箱 DLL 换成坏字节 → 报 `Bad IL format`；换回好文件 → 正常加载。
- 所以插件热升级流程 = `POST .../syncplugins` → `/hr load <插件>`，**无需重启**。
- `PGame-TSManager.exe --syncplugins` 已同步修正：除根目录镜像外，也把总库同步进每个沙箱。

### 结构化插件热更

`POST /tsm/servers/{id}/plugins/reload` body `{ "plugin": "FixTools" }`：

- 先查运行沙箱 `ServerPlugins\<plugin>.dll` 是否存在；
- 存在 → 200 `{ok:true, plugin:"FixTools", found:true, output:"…已加载"}`；
- 不存在 → 404 `{ok:false, error:"plugin_not_found", plugin:"…"}`（比 `/command` 的结构化程度更高）。

## 非托管同名进程检测（P2）

TSM 每 ~30s 经 WMI 扫描 `TShock.Server.exe`，凡是**不属于任何 TSM 容器**的进程，发一次事件：

```json
{ "Type": "server.process.conflict", "ServerId": null,
  "Payload": { "serverId": null, "pid": 7436, "name": "TShock.Server", "port": 0, "managed": false } }
```

- 只告警：**不清理、不拒绝启动**（避免误杀）。
- 能映射时用命令行 `-port` 回填 `serverId`（映射不到则为 `null`）。
- 同一 PID 只报一次；进程消失后再出现会重新告警。

## ExternalProcess

`externalProcesses` 是通用外部进程列表。公共 Core 只定义结构，默认列表为空，私人条目由个人版 overlay 配置。

```json
{
  "kind": "external-process",
  "id": "example",
  "name": "Example",
  "enabled": true,
  "workdir": "C:\\Example",
  "exe": "Example.exe",
  "processName": "",
  "args": [],
  "stopArgs": [],
  "healthMode": "http",
  "healthPorts": [1234],
  "healthUrl": "http://127.0.0.1:1234/api/v1/health",
  "logDir": "C:\\Example\\Logs",
  "logActiveSeconds": 300,
  "watchdog": "none",
  "autoRestart": false
}
```

## 稳定 serverId

每服 profile 应显式写 `id`。旧 profile 缺 `id` 时 TSM 会生成稳定 fallback。

```json
{
  "id": "server-b",
  "name": "生存2",
  "rootPath": "Servers\\Profiles\\2.生存2"
}
```

## 状态字段与事件序号（客户端去重必读）

`GET /tsm/status`：

```json
{
  "ok": true,
  "contractVersion": "tsm.control.v1",
  "managerRunning": true,
  "managerStartedAt": "2026-10-08T23:52:10.1234567+08:00",
  "eventEpoch": 1760025130123,
  "serverCount": 3, "runningCount": 3, "externalCount": 2,
  "at": "..."
}
```

- **`Seq` 跨进程重启单调递增**：`ControlEventHub` 用毫秒时间戳作为启动下界，重启后新事件 seq 一定大于上一次运行的所有 seq。（曾经每次从 1 重计，会把客户端落盘的 seq 去重基线顶掉、静默丢事件。）
- `eventEpoch` = 本进程的 seq 下界；`managerStartedAt` = 进程启动时间。客户端可用二者区分“没有新事件”与“TSM 重启过”，从而安全恢复 `Last-Event-ID` 断线补发。
- `GET /tsm/servers` 每项：`id, name, state, gamePort, restPort, restTokenAlias, world, pid, rebuilding, at`。
  **停机服的 `pid` 为 `null`**（客户端字段需可空，例如 `int?`）。

## 当前 serverId ↔ 服务器（2026-10-09 起统一为数字）

| id | 服 | 游戏端口 | REST | restTokenAlias |
|---|---|---|---|---|
| `0` | 鸽子直播服 | 2020 | 7877 | `0-rest` |
| `1` | 流光城 | 2021 | 7878 | `1-rest` |
| `2` | 泰拉大陆 | 2023 | 7879 | `2-rest` |
| `3` | 流光神域 | 2024 | 7880 | `3-rest` |

- **旧 id 已废弃**：`pigeon-live / liuguang-city / terra-mainland / liuguang-realm` 不再作为 `id`（但按名字匹配仍可用）。
- `/tsm/servers` 每项额外带 **`label`**（下拉框同款，含序号，如 `"0. 鸽子直播服"`、`"1. 流光城"`）。
- ⚠ **`restTokenAlias` 由 id 派生** = `"<id>-rest"`：**改 id 会连带改别名**。客户端若按别名存直连表，改 id 时必须同步（例如 `liuguang-city-rest` → `1-rest`）。
