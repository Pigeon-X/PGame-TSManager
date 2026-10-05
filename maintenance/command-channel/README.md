# 服务器指令通道（TSM → TShock）

## 结论（2026-10-06 定稿）

TSM 给服务器发指令**只用一条路**：

```
POST http://127.0.0.1:<REST端口>/v3/server/rawcmd
     ?token=<外部应用令牌>&cmd=<指令>
```

- 端口：每服自己在 `1.PigeonServers\<服>\config.json` 的 `REST端口`
  （流光城 7878 / 泰拉大陆 7879 / 流光神域 7880）
- 令牌：该服 `tshock\config.json` → `Settings.Rest外部应用令牌字典` 里的键名
- **指令必须带前导 `/`**（`/help` 可以，`help` 会回 `输入的命令无效`）→ TSM 会自动补
- 返回：

```json
{ "status": "200", "response": [ "命令输出第 1 行", "第 2 行" ] }
```

TSM 会把 `response` 数组拼成多行显示在控制台面板（并去掉 `[c/xxxxxx:]` 颜色标记）。

## ⚠ 曾经踩的坑：端点是 v3，不是 v2

TShock 6.2.1 源码 `TShockAPI\Rest\RestManager.cs`：

```csharp
Rest.Register(new SecureRestCommand("/v3/server/rawcmd", ServerCommandV3, RestPermissions.restrawcommand));
...
Rest.RegisterRedirect("/server/rawcmd", "/v3/server/rawcmd");
```

- `/v2/server/rawcmd` **不存在** → 必然 404（之前「TSM 发不出指令」的真正原因）
- `/server/rawcmd` 是注册的重定向，可用（TSM 里作为兜底）
- `/v3/server/rawcmd` 需要 REST 令牌对应的用户组带 `restrawcommand` 权限（superadmin 默认有）
- 服务端实现就是 `Commands.HandleCommand(TSRestPlayer, cmd)`，与在控制台敲完全等价

## 另外两条路为什么不用

| 通道 | 结论 |
|---|---|
| 重定向 stdin | ❌ TShock 6.2 明确拒绝：`Input redirection is not supported, exiting the process immediately.` |
| 控制台按键注入（AttachConsole + WriteConsoleInput） | ❌ 事件写入成功（含虚拟键码），但服务器**完全不读**；可见控制台里也一样 → 输入被 OTAPI detour 接管，不走标准 console input |

## TSM 侧实现

- `PGame-TSManager\ServerContainer.cs`
  - `SendText(msg)`：补前导 `/` → 走 REST；失败才兜底写 stdin
  - `SendCommandViaRest(cmd, out output)`：不依赖界面/进程，可复用
  - `FormatRestResponse(body)`：解析 `response` 并去掉 Terraria 颜色标记
- `PGame-TSManager\Cli.cs`：无界面发指令，方便远程运维与自检

```
PGame-TSManager.exe --send <服务器名> <指令>
例：PGame-TSManager.exe --send 流光城 "/help"
    PGame-TSManager.exe --send 泰拉大陆 "/save"
```

结果写入程序目录 `sendresult.txt`，退出码 0 = 成功。

## 实测（2026-10-06，远程 114.28.145.124）

```
流光城   /help    exit=0  → 命令列表 (1/5)
泰拉大陆 /help 2  exit=0  → 命令列表 (2/7)
流光神域 /help 3  exit=0  → 命令列表 (3/7)
```
三个服 REST 7878/7879/7880 全部 200。