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

### ⚠ POST 必须带 body 或 `Content-Length: 0`（否则直接 411）

底层是 `HttpListener`：**没有 body 且没有 `Content-Length` 的 POST 会在路由之前就被拒掉**，
返回 `HTTP/1.1 411 Length Required`（HTML 错误页，不是 JSON），请求**根本不会执行**。

实测踩坑（2026-10-11 维护窗口）：`curl -X POST http://127.0.0.1:8765/tsm/servers/2/restart`
→ `411 Length Required`，服务器完全没动，而调用方看到的是「响应异常」而不是「重启失败」。

各种客户端的正确写法：

```powershell
# PowerShell（Invoke-RestMethod / Invoke-WebRequest）自动带 Content-Length: 0，可直接用
Invoke-RestMethod -Method Post -Uri "$base/tsm/servers/2/restart" -Headers $h
```

```text
# curl：加 -d "" 或显式声明长度
curl -s -X POST -H "X-TSM-Token: $T" -d "" http://127.0.0.1:8765/tsm/servers/2/restart
curl -s -X POST -H "X-TSM-Token: $T" -H "Content-Length: 0" http://127.0.0.1:8765/tsm/servers/2/restart
```

返回体：`{"ok":true,"id":"2","state":"restarting"}`（HTTP 202）。`restart` 是**异步**的
（停服 → 等进程退出 → 起服），调用方应轮询 `GET /tsm/servers` 看 `state`/`pid` 变化，不要用响应本身判断完成。

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
- 存在 → 200 `{ok:true, plugin:"FixTools", found:true, output:"…已加载", verifiedBy:"rest"}`；
- 不存在 → 404 `{ok:false, error:"plugin_not_found", plugin:"…"}`（比 `/command` 的结构化程度更高）。

#### ⚠ 成功判定是「两条腿」（2026-10-11 实测修正）

`PGameAPI` 这类插件热重载时会**重建自身全部模块**，期间 TShock 主线程阻塞、连接被重置 →
TSM 到 TShock 的 REST 请求直接抛 `An error occurred while sending the request.`（**不是 HTTP 4xx**），
但插件**其实加载成功**（日志有 `[HotReload] 已加载 PGameAPI v1.0.0`）。
旧实现只看 REST 结果 → 对 PGameAPI 返回 400，把成功报成失败（Bot 的 `/tsm 插件` 会误报）。

现在的判定：

1. REST 通 → 200，`verifiedBy:"rest"`；
2. REST 抛错 → 回读该服沙箱日志 `Core\_runtime\<服>\Logs\<最新>.log`，最多等 8 秒，
   看到 `…已加载 <插件>` → **200**，`verifiedBy:"log"`，并把命中的日志行放进 `verifiedLine`；
3. 两条都没成 → 400，字段含义：

| 字段 | 含义 |
|---|---|
| `ok` | 最终判定（`true` 才算加载成功） |
| `found` | 沙箱里是否有该 DLL（`false` 时是 404） |
| `output` | TShock 命令回显（REST 成功时才有） |
| `error` | REST 层错误文本（`null` = REST 成功） |
| `httpStatus` | TShock 侧 HTTP 码；**`null` = 连响应都没拿到**（连接被重置/超时） |
| `verifiedBy` | `rest` / `log` / `null`（未通过任何一条） |
| `verifiedLine` | `verifiedBy=log` 时命中的那行日志原文 |

客户端建议：**`ok` 为准**；`verifiedBy:"log"` 属正常成功，无需再自查。
插件显示名与 DLL 名不一致时（例：`FixTools.dll` 的显示名是「流光系统」），
日志行按插件名匹配不到会退化为「本窗口内任意一次 `已加载`」，此时请以返回的 `verifiedLine` 自行确认。

#### ⚠ 会阻塞主线程的热重载一律走 `/plugins/reload`，不要用 `/command` 发 `/hr load`

`/command` 是**通用指令通道**，不带上面对日志做复核的判定：同一条 TSM→TShock 通道被 reset 时，
它只能如实回 400（`{"ok":false,"error":"An error occurred while sending the request."}`），
而插件其实已经加载成功 —— 调用方会把成功误报成失败。

```text
✅ POST /tsm/servers/{id}/plugins/reload  {"plugin":"PGameAPI"}   → 200 verifiedBy:"log"
❌ POST /tsm/servers/{id}/command        {"command":"/hr load PGameAPI"} → 400（假失败）
```

`/command` 仍适合普通指令（`/save`、`/playing`、`/warp` 等）；只有**热重载**这类会重建模块、阻塞主线程的操作要走结构化端点。

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
