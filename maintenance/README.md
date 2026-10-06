# PGame-TSManager Maintenance

| 子目录 | 职责 |
| --- | --- |
| tshock-update | 独立 TShock 新版本适配、补丁、回归记录；不内置到 TSM |
| config-translation | TShock config 中文映射（含 TransferPatch.json 权威映射表） |
| rest-fixes | REST 配置和令牌字段修复（用户名 / 用户组） |
| plugin-maintenance | 独立插件维护入口，指向 PigeonPlugins |
| ssc-lock | SSC 英文配置冻结 |
| help-lock | /help 指令冻结 |
| tools | 维护工具（Test-TShockConfig.ps1 只读校验） |

## 更新边界

TShock 更新属于独立维护流程：GitHub Actions 负责构建维护包，`tshock-update` 负责官方包接入、
汉化映射、REST 修复和回归验证，PGame-TSManager 负责服务器生命周期管理。更新工具不由 TSM 窗口调用。

每次修改的固定顺序是：Git 提交 → Actions 构建 → 远程源测试端部署 → 可见重启和三服验证 → 桌面镜像同步 → 测试群上报。

## 固定规则（不可违反）

1. `/help` 不修改、不禁用、不覆盖。
2. SSC（`sscconfig.json`）保持英文，不翻译、不改键、不改字段。
3. `config.json` 键名中文化。
4. REST 令牌字段固定为中文「用户名 / 用户组」。

## 一键校验

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test-TShockConfig.ps1 -ServerPath '<服务端目录>'
```

一条命令同时校验上面 2 / 3 / 4 三条规则，退出码 0 = 通过。
## 目录

| 目录 | 用途 |
|---|---|
| `command-channel\` | **TSM → TShock 指令通道**（REST `/v3/server/rawcmd`、`--send`） |
| `config-translation\` | TShock config 汉化映射（REST 令牌、SSC 除外规则） |
| `rest-fixes\` | REST 令牌字段与端点修复 |
| `ssc-lock\` | SSC 保留英文的规则与校验 |
| `help-lock\` | /help 不被覆盖的规则 |
| `plugin-maintenance\` | 插件总库审计与依赖归类 |
| `tshock-update\` | TShock 上游跟进流程与行为分析 |
| `tools\` | 迁移 / 清理 / 校验脚本 |

## 新增服务器

目录名带序号，管理器按序号自动排序 + 自动发现：

```
1.PigeonServers\
  1.流光城\
  2.泰拉大陆\
  3.流光神域\
  4.新服名\        ← 新增就是这个规则，下一台就是 5.
```

新增步骤（也可以直接用脚本）：

```powershell
.\maintenance\tools\New-PigeonServer.ps1 -Root "D:\59934\Desktop\PGame-TSManager" `
  -Name "生存服" -Port 2025 -RestPort 7881 -Template "3.流光神域"
```

脚本会：算下一个序号 → 建目录 → 复制模板的 `tshock\` → 写好 TSM 清单 `config.json` → 打印还要手改的项（REST 端口、数据库、世界文件）。

要点：
- 管理器启动时会**自动发现** `1.PigeonServers\` 下「有 config.json 且写了端口」的目录，
  **不用再改管理器 config.json**。
- 排序按目录名开头的数字：`1.` → `2.` → `10.`（数字大小，不是字符串）。
- 服务器目录改名后，`_runtime\<服>\tshock` 这个目录联接会被自动检测并重建
  （悬空联接会让插件以为配置丢了，CustomPlayer 那种会 `Console.ReadKey()` 直接把服务器启动搞崩）。

## 看门狗与告警（2026-10-06）

### 看门狗（异常退出自动重启）

服务器**非手动**退出时，自动按顺序处理：

1. 记录退出码与运行时长 → 写日志 + 发告警
2. 若运行时长 ≥ `watchdogStableSeconds`（默认 300 秒）→ 认为不是崩溃循环，重启计数清零
3. 未达上限 → 等 `watchdogRestartDelaySeconds`（默认 8 秒）后自动重启，并再发一条告警
4. 达到 `watchdogMaxRestarts`（默认 3 次）→ 停止重启并告警「请人工检查」
5. **人工点「启动本服」会把计数清零**（否则达到上限后以后都不会再自动重启）

手动点「停止本服」不会触发看门狗（内部 `_userStopRequested` 标记）。

### 告警（发到测试群）

走远程现成的上报脚本：

```
AI维护文件\12-机器人\机器人上报测试群.ps1 -Text "<消息>" -GroupId 1125570228
```

触发点：
- 服务器异常退出 / 正在自动重启 / 反复崩溃放弃重启
- 顺序启动时某台启动失败或超过就绪超时
- 日志里出现致命关键字（`Unhandled exception`、`Startup aborted`、`Fatal`、`Failed to load assembly`、`OutOfMemory` 等），每服 5 分钟最多一次

限流：`alertMinIntervalSeconds`（默认 60 秒）内的多条告警只发第一条，避免刷屏。

### 配置

```json
"watchdogEnabled": true,
"watchdogMaxRestarts": 3,
"watchdogRestartDelaySeconds": 8,
"watchdogStableSeconds": 300,
"alertEnabled": true,
"alertScript": "",
"alertGroupId": 1125570228,
"alertMinIntervalSeconds": 60
```

`alertScript` 留空会**自动找**桌面 `AI维护文件\12-机器人\机器人上报测试群.ps1`。

### 控制台落盘

管理器面板里所有内容（含看门狗/告警行）会同时写到：

```
Logs\<服名>-yyyyMMdd.log
```

每行带 `[HH:mm:ss]` 时间戳，方便事后排查与导出。
