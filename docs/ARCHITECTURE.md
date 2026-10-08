# PGame-TSManager 架构边界

> 生效日期：2026-10-08
> 适用仓库：`Pigeon-X/PGame-TSManager`（通用 Core）、`Pigeon-X/PGame-TSManager-Personal`（个人 overlay）

---

## 1. 定位

**PGame-TSManager 是 TShock 多进程控制面，不是业务层。**

### TSM 负责

- TShock 进程启动 / 停止 / 重启 / 顺序启动
- 每服沙箱（`_runtime`）、插件总库与每服插件清单同步
- 备份、回滚、日志
- 管理器 `config.json`、每服清单 `config.json`、`tshock\config.json` / `sscconfig.json` / SSC
- 看门狗、健康状态、控制事件
- 通用指令派发（`--send` / TShock REST `rawcmd`）

### TSM 不负责

- RPG 等级、职业、任务、商店、经济
- QQ / OneBot / 群指令 / 玩家绑定
- 流光维度跨服、切图、世界路由、进度同步
- 任意插件的业务算法
- 玩家数据库、机器人账本、RPG 数据库的直属读写

---

## 2. 仓库分层

| 层 | 仓库 | 内容 |
| --- | --- | --- |
| Core | `Pigeon-X/PGame-TSManager` | 通用管理器、维护工具、TShock 模板、扩展点 |
| Personal | `Pigeon-X/PGame-TSManager-Personal` | `overlay/`：私人服务器配置、私人插件、Bot / 维度 / RPG 对接内容 |
| 版本锚点 | Personal `core.lock.json` | 锁定所使用的 Core 提交 |
| 业务插件 | PGameAPI / PigeonRPG / Dimensions / PigeonBot 等各自仓库 | 通过 REST / JSON / 事件与控制面交互 |

**硬规则**

- 私人内容只出现在 Personal 仓库的 `overlay/` 里。
- Core 的代码、默认配置、数据文件中不出现私人插件名、私人服名、私人群号、私人路径、私人令牌。

---

## 3. 对接原则

### 3.1 一个进程只能有一个生命周期 owner

TShock 进程的启停只归 TSM（界面 / CLI / 看门狗）。

- 其他项目**不得**直接 kill 进程，也**不得**用 `/off`、`/off-nosave` 之类指令结束服务器。
- 需要重启时，调用 TSM 的控制入口（`--send` / 未来的控制 API），或通知 TSM 由它执行。
- 手动停止的服务器不触发看门狗；异常退出才触发自动重启。

### 3.2 一个配置文件只能有一个写入 owner

| 文件 | 写入 owner |
| --- | --- |
| 管理器 `config.json` | TSM |
| `schedules.json` | TSM |
| `1.PigeonServers\<服>\config.json`（每服清单） | TSM |
| `tshock\config.json`、`tshock\sscconfig.json` | TSM（服务器设置窗口） |
| 插件自己的配置（如 `tshock\TSWeb\*.json`、`tshock\PigeonRPG\*.json`） | 对应插件 |

跨 owner 只读不写。需要别的 owner 改配置时，走对方提供的接口。

### 3.3 跨项目交互必须走稳定契约、事件或控制 API

允许：HTTP / REST / JSON 文件契约 / 事件回调 / CLI 控制入口。
禁止：直接调用对方的内部类、共享可变状态、按对方实现细节硬编码路径。

### 3.4 禁止跨仓库硬引用实现类

优先 HTTP / REST / JSON / 事件。Core 的 `.csproj` 不引用任何业务插件或机器人程序集。

### 3.5 个人版专用对接只放个人版 overlay

公共 Core 只保留通用扩展点，私人对接内容一律进 `PGame-TSManager-Personal/overlay/`。

---

## 4. Core 提供的通用扩展点

| 扩展点 | 位置 | 用途 |
| --- | --- | --- |
| 每服清单 | `1.PigeonServers\<服>\config.json` | 启动参数 + 插件清单；新增个人服只加数据，不改代码 |
| 插件用途说明 | `Core\Data\plugin-descriptions.json`（公共默认）<br>`Core\Data\plugin-descriptions.local.json`（overlay 覆盖） | 私人插件的中文说明由 overlay 提供；相同键时 local 覆盖默认 |
| 物品名称表 | `Core\Data\item-names.zh-CN.json` | 服务器设置窗口的中文物品检索 |
| 告警上报 | `config.json` → `alertEnabled` / `alertScript` / `alertGroupId(s)` | 外部脚本契约：`<script> -Text "<正文>" -GroupId <群号>`；三项留空即关闭 |
| 模板服 | `Servers\Profiles\<模板>` | 新建服务器向导的模板来源 |
| 控制 API | `--send <服> <命令>` / 本地 `ControlApiServer` / TShock REST `rawcmd` | 外部项目发指令和生命周期请求的通道；默认关闭 |
| overlay 应用 | Personal `scripts\Build-Local.ps1` | 在构建期把 `overlay/` 叠加到 Core 产物上 |

## 5. 启动/换图边界（2026-10-08 增补）

- `.wld` 不存在时，TSM 允许在启动参数中自动补 `-autocreate`：优先读取启动参数，其次读取 profile `自动建图`，最后读取该服运行目录 `server.properties` 的 `autocreate`。
- TSM 不直接写 `server.properties`；`autocreate` 的唯一业务写入者仍应是 TSM 的世界重建控制入口，FixTools 等插件不得直接改该文件。
- 自动建图启动会进入看门狗保护窗口：小图至少 300 秒、中图至少 480 秒、大图至少 600 秒，并可由 `watchdogWorldBuildSuppressSeconds` 再拉长。

---

## 6. 提交前自检

- [ ] Core 代码里没有私人插件名 / 私人服名 / 私人 QQ 群号 / 私人桌面路径
- [ ] Core 默认配置里没有私人令牌、私人端口、私人数据库
- [ ] 私人文件只在 Personal 仓库的 `overlay/`
- [ ] 新增功能若属业务层，落到对应插件仓库而不是 TSM
- [ ] 跨项目调用只走 REST / JSON / 事件 / CLI 控制 API
