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
world.rebuild.failed
process.started
process.stopped
process.unhealthy
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
