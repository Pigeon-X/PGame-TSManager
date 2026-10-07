# PGame-TSManager

<p align="center">
  <strong>Windows 平台 TShock 多服务器管理器</strong>
</p>

<p align="center">
  <a href="https://github.com/Pigeon-X/PGame-TSManager/actions/workflows/build.yml">
    <img src="https://github.com/Pigeon-X/PGame-TSManager/actions/workflows/build.yml/badge.svg" alt="Build status">
  </a>
  <a href="https://github.com/Pigeon-X/PGame-TSManager/releases">
    <img src="https://img.shields.io/github/v/release/Pigeon-X/PGame-TSManager?include_prereleases&label=build&color=2f81f7" alt="Build release">
  </a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/TShock-6.2.x-4B8BBE" alt="TShock 6.2.x">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9">
</p>

<p align="center">
  <a href="https://github.com/Pigeon-X/PGame-TSManager/releases">下载构建</a>
  ·
  <a href="https://github.com/Pigeon-X/PGame-TSManager/actions">构建记录</a>
  ·
  <a href="https://github.com/Pigeon-X/PGame-TSManager/issues">问题反馈</a>
</p>

> [!NOTE]
> 本仓库维护的是**通用版**：管理器、维护工具和两套 TShock 服务器模板。
> RPG、私人插件、个人服务器配置不会进入通用版，由独立个人版覆盖层维护。

## 当前通用版

通用包现在默认包含两套可直接被管理器识别的 TShock 模板：

| 模板 | 目录 | 游戏端口 | REST 端口 |
| --- | --- | ---: | ---: |
| 生存 | `Servers\Profiles\1.生存` | `7777` | `7878` |
| 生存2 | `Servers\Profiles\2.生存2` | `7778` | `7879` |

模板包含：

- PGame-TSManager 的 `config.json`
- 中文 TShock `tshock\config.json`
- SQLite 默认数据库配置
- REST API 默认配置
- 默认必需插件：`TShockAPI.dll`、`HotReload.dll`
- GitHub 构建时自动下载官方 TShock Windows x64 运行时并注入 `Core`
- 公共占位令牌，不包含私人令牌、MySQL 密码或玩家数据

通用包不包含世界文件。使用者只需要把世界文件放入 `Servers\Worlds`。

## 项目定位

PGame-TSManager 用一个窗口管理多个 TShock 服务端。它负责：

- 服务器启动、停止和顺序启动
- 控制台查看和服务器指令发送
- 插件总库与每服插件清单同步
- TShock config、SSC、初始物品可视化编辑
- 启动前备份、回滚中心、看门狗和异常告警
- 自检、维护工具和 GitHub Actions 自动构建

它不替代 TShock 本身，也不会把世界、数据库、日志或玩家数据提交到 Git。

## 四个目录

运行端统一使用四个可见目录：

```text
PGame-TSManager/
├─ PGame-TSManager.exe
├─ config.json
├─ Core/
│  ├─ TShock.Server.exe
│  ├─ GeoIP.dat
│  ├─ bin/
│  ├─ i18n/
│  ├─ runtimes/
│  ├─ x64/
│  ├─ Data/
│  ├─ Backups/
│  ├─ Logs/
│  └─ _runtime/
├─ Servers/
│  ├─ Profiles/
│  │  ├─ 1.生存/
│  │  │  ├─ config.json
│  │  │  └─ tshock/config.json
│  │  └─ 2.生存2/
│  │     ├─ config.json
│  │     └─ tshock/config.json
│  └─ Worlds/
├─ Plugins/
└─ Tools/
   └─ maintenance/
```

| 目录 | 用途 |
| --- | --- |
| `Core` | TShock 运行时、依赖、数据、日志、备份和运行沙箱 |
| `Servers` | 每服配置、TShock 配置和世界文件 |
| `Plugins` | 统一插件总库 |
| `Tools` | 维护工具、模板和脚本 |

## 快速开始

1. 从 [Releases](https://github.com/Pigeon-X/PGame-TSManager/releases) 下载通用包。
2. 解压到目标目录。
3. 通用包已经准备好 `Core` TShock 运行时，不需要手动复制：

```text
Core/
├─ TShock.Server.exe
├─ GeoIP.dat
├─ bin/
├─ i18n/
├─ runtimes/
└─ x64/
```

4. 将世界文件放入 `Servers\Worlds\生存.wld` 和 `Servers\Worlds\生存2.wld`。
5. 按需修改两个模板里的 `tshock\config.json`。
6. 双击 `PGame-TSManager.exe`。
7. 首次部署执行：

```powershell
PGame-TSManager.exe --selfcheck
```

检查结果会写入 `selfcheck.txt`。

## 服务器清单

根目录 `config.json` 的 `serverProfiles` 负责服务器列表：

```json
{
  "name": "生存",
  "rootPath": "Servers\\Profiles\\1.生存",
  "executable": "TShock.Server.exe",
  "arguments": "",
  "enabled": true,
  "remark": "通用模板 7777 / REST 7878",
  "plugins": [],
  "pluginLibrary": ""
}
```

每服自己的 `config.json` 描述世界、端口、语言和插件清单：

```json
{
  "服务器名称": "生存",
  "启用": true,
  "世界": "生存.wld",
  "语言": 7,
  "端口": 7777,
  "REST端口": 7878,
  "最大玩家": 16,
  "IP": "0.0.0.0",
  "密码": "",
  "启动参数": "",
  "插件": [],
  "插件总库": "Plugins",
  "覆盖插件目录": true,
  "备注": "通用模板 7777 / REST 7878"
}
```

管理器会自动发现 `Servers\Profiles` 下带有 `config.json` 且填写了端口的服务器目录。

## 功能概览

| 功能 | 说明 |
| --- | --- |
| 多服务器管理 | 一个窗口切换不同 TShock 服务端 |
| 顺序启动 | 上一台真正就绪后再启动下一台，降低同时加载世界的压力 |
| 控制台 | TShock 输出、插件、警告、错误和成功状态分类着色 |
| 指令发送 | 支持当前服发送、全部服发送和 `--send` 命令 |
| 插件同步 | 按每服清单从 `Plugins` 同步，停用插件移入 `ServerPlugins.disabled` |
| 配置管理 | TShock、SSC、初始物品和更多设置分页编辑 |
| 备份与回滚 | 启动前备份，支持查看和恢复管理器更新备份 |
| 看门狗 | 异常退出自动重启，连续失败后停止并告警 |
| 内存压缩 | 空服时压缩工作集，不关闭端口、不中断首次进入 |
| 单实例 | 重复双击 exe 只唤醒已有管理器，不创建第二个窗口 |
| 自定义标题栏 | PGame-TSManager 标题、圆角 Logo、居中窗口按钮 |

## 常用命令

| 命令 | 用途 |
| --- | --- |
| `PGame-TSManager.exe --startall` | 启动管理器并顺序启动全部服务器 |
| `PGame-TSManager.exe --syncplugins` | 只同步插件，不启动服务器 |
| `PGame-TSManager.exe --selfcheck` | 无界面自检 |
| `PGame-TSManager.exe --send <服务器名> <指令>` | 通过 REST 发送服务器指令 |
| `PGame-TSManager.exe --nowindow` | 强制无界面运行 |

## 插件规则

- `Plugins` 是统一插件总库。
- 每台服务器加载哪些插件，由该服 `config.json` 的 `插件` 清单决定。
- 未列出的第三方插件会被移动到停用目录，不会直接删除。
- `TShockAPI.*` 由 TShock 自身保留，不参与插件同步和停用。
- `TShockAPI.dll` 和 `HotReload.dll` 默认必需，插件开关不能取消。
- 插件依赖 DLL 放入 `Core\bin`，不要混入 `Plugins`。

## TShock 维护规则

- TShock 更新通过 `Tools\maintenance\tshock-update` 独立处理。
- `config.json` 使用中文键。
- REST 令牌字段使用「用户名 / 用户组」。
- `sscconfig.json` 保持英文，不翻译、不改键、不改字段。
- `/help` 保持 TShock 原逻辑，不覆盖、不禁用。

## 构建

源码构建需要 .NET 9 SDK：

```powershell
dotnet build PGame-TSManager.sln -c Release

dotnet publish PGame-TSManager/PGame-TSManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/manager
```

通用包和模板包会自动调用 `Add-TShockTemplates.ps1` 写入两套 TShock 模板。

## 发布包

| 包 | 用途 |
| --- | --- |
| `PGame-TSManager-universal.zip` | 管理器、维护工具和两套 TShock 模板 |
| `PGame-TSManager-template.zip` | 管理器模板和两套 TShock 模板 |
| `PGame-TSManager-source.zip` | 源码、工作流和维护脚本 |

每次 `main` 推送会生成测试 Release；推送 `v*` 标签会生成正式 Release。
仓库只保留最新 10 条 Release，更早的 Release 和对应 tag 会在构建后自动清理。

## 通用版与个人版

| 内容 | 通用版 | 个人版 |
| --- | --- | --- |
| 管理器与 UI | 有 | 有 |
| 维护工具 | 有 | 有 |
| TShock 模板 | 有 | 有自己的三服配置 |
| RPG 插件 | 无 | 有 |
| PGameAPI/PChrome | 无 | 有 |
| 个人服务器配置 | 无 | 有 |
| 世界、数据库、玩家数据 | 不进入 Git | 不进入 Git |

## 致谢

- 旧版多服管理器思路来自 [cc004/TSManager](https://github.com/cc004/TSManager)。
- TShock 上游由 [Pryaxis/TShock](https://github.com/Pryaxis/TShock) 维护。
- 本项目由 [Pigeon-X](https://github.com/Pigeon-X) 公开维护。
