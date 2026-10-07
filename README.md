# PGame-TSManager

<p align="center">
  <strong>Windows 平台 TShock 多服务器管理、维护与兼容适配工具</strong>
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
> 本项目由 [Pigeon-X](https://github.com/Pigeon-X) 公开维护，持续跟进 TShock 6.2.x，
> 保留旧版 TSManager 的多服管理思路，并整理配置汉化、REST 修复、插件同步与一键构建流程。

## 项目定位

PGame-TSManager 用一个 Windows 管理器统一维护多个 TShock 服务端目录。它负责启动、停止、控制台查看、指令发送、插件同步、配置备份、自检和更新辅助，不替代 TShock 本身。

| 项目 | 地址 |
| --- | --- |
| 当前公开维护仓库 | [Pigeon-X/PGame-TSManager](https://github.com/Pigeon-X/PGame-TSManager) |
| 当前构建下载 | [GitHub Releases](https://github.com/Pigeon-X/PGame-TSManager/releases) |
| 旧版 TSManager 原作者项目 | [cc004/TSManager](https://github.com/cc004/TSManager) |
| TShock 官方项目 | [Pryaxis/TShock](https://github.com/Pryaxis/TShock) |

## 功能概览

| 功能 | 说明 |
| --- | --- |
| 多服务器管理 | 一个窗口管理多个 TShock 服务端，可切换查看日志 |
| 启动控制 | 支持单服启动、顺序全部启动、全部停止 |
| 配置管理 | 可视化调整服务器配置、插件清单、初始物品和 SSC |
| 插件同步 | 按每服插件清单同步；未启用的插件移入停用目录，不删除 |
| 运行保护 | 启动前备份、崩溃检测、自动重启和运行日志 |
| 指令通道 | 控制台显示、REST 指令发送、热重载和插件状态检查 |
| 部署自检 | 无界面检查服务器目录、服务端文件与配置完整性 |
| 自动构建 | GitHub Actions 自动打包并发布到 Releases |

## 运行要求

- Windows 10 / Windows 11
- TShock 6.2.x
- 自包含发布包不需要额外安装 .NET
- 源码构建需要 .NET 9 SDK

## 快速开始

1. 从 [Releases](https://github.com/Pigeon-X/PGame-TSManager/releases) 下载部署包。
2. 解压到目标目录。
3. 编辑根目录 `config.json`，确认 `serverProfiles` 指向正确的服务端目录。
4. 双击 `PGame-TSManager.exe`。
5. 在服务器列表选择目标服务器，点击“启动本服”或“全部启动”。
6. 首次部署后执行 `PGame-TSManager.exe --selfcheck`，确认目录完整。

## 发布包

| 包 | 用途 |
| --- | --- |
| `PGame-TSManager-universal.zip` | 通用管理器与维护工具 |
| `PGame-TSManager-template.zip` | 自包含管理器与空配置模板 |
| `PGame-TSManager-source.zip` | 源码与维护脚本 |

每个 ZIP 内附带 `使用说明.txt` 和 `更新内容.txt`，Releases 页面也会提供这两个文件。
每次 `main` 构建会生成 `v<版本>-build.<运行号>` 预发布并附带上述 ZIP。推送 `v*` 标签时进入正式 Release 流程。

## 常用命令

| 命令 | 用途 |
| --- | --- |
| `PGame-TSManager.exe --startall` | 启动管理器并顺序启动全部服务器 |
| `PGame-TSManager.exe --syncplugins` | 只同步插件，不启动服务器 |
| `PGame-TSManager.exe --selfcheck` | 无界面自检，结果写入 `selfcheck.txt` |
| `PGame-TSManager.exe --nowindow` | 强制无界面运行 |

## 目录说明

```text
PGame-TSManager/
├─ PGame-TSManager.exe       管理器主程序
├─ config.json               服务器列表和运行策略
├─ 1.PigeonServers/          各服务器独立目录
├─ Plugins/                  插件统一维护目录
├─ bin/                      TShock 与插件依赖库
├─ Data/                     中文物品名等通用数据
├─ Backups/                  配置备份
├─ _runtime/                 运行沙箱与控制台日志
└─ maintenance/              更新、校验和维护工具
```

| 目录或文件 | 用途 |
| --- | --- |
| `PGame-TSManager.exe` | 管理器主程序 |
| `config.json` | 服务器列表和运行策略 |
| `1.PigeonServers` | 各服务器独立目录 |
| `Plugins` | 插件统一维护目录 |
| `bin` | TShock 与插件依赖库 |
| `_runtime` | 运行沙箱、控制台日志和临时文件 |
| `Backups` | 配置备份 |
| `Data` | 中文物品名等通用数据 |
| `maintenance` | 更新、校验和维护工具 |

## 服务器配置

`config.json` 中的 `serverProfiles` 描述服务器。管理器只读取配置并启动对应目录，不自动改写服务器自身配置。

```json
{
  "name": "示例服",
  "rootPath": "D:\\TShockServers\\Example",
  "executable": "TShock.Server.exe",
  "arguments": "",
  "enabled": true,
  "remark": "示例服务器"
}
```

| 字段 | 说明 |
| --- | --- |
| `name` | 管理器显示名称 |
| `rootPath` | 现有服务端目录 |
| `executable` | 服务端可执行文件 |
| `arguments` | 原样透传的启动参数 |
| `enabled` | 是否允许启动 |

## 插件加载规则

- `Plugins` 是插件总库。
- 每台服务器实际加载哪些插件，以该服务器 `config.json` 的插件清单为准。
- 启动前由管理器同步到运行目录。
- 不在清单中的插件移入停用目录，不直接删除。
- 插件依赖 DLL 放在 `bin`，不要混入插件本体目录。

## 备份与安全

- `backupBeforeStart` 开启后，启动前备份服务器关键配置。
- `backupKeep` 控制保留份数。
- 世界文件、数据库、日志和玩家数据不进入 Git 仓库。
- 自检和配置校验默认只读，不修改服务器文件。

## 维护边界

- TShock 更新使用 `maintenance/tshock-update` 的独立流程。
- 配置中文映射使用 `maintenance/config-translation`。
- REST 修复使用 `maintenance/rest-fixes`。
- SSC 保持英文，不翻译、不改键、不改字段。
- `/help` 保持原逻辑，不覆盖、不禁用。
- 通用底层由本仓库维护；个人服配置、私人插件和 RPG 数据由独立覆盖层维护。

## 构建

```powershell
dotnet build PGame-TSManager.sln -c Release

dotnet publish PGame-TSManager/PGame-TSManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/manager
```

## 自动构建与发布

`.github/workflows/build.yml` 在 `main`、Pull Request、手动运行和 `v*` 标签触发：

| 事件 | 结果 |
| --- | --- |
| `main` 推送 / 手动运行 | 创建 `v<版本>-build.<运行号>` 预发布并上传 ZIP |
| `v*` 标签 | 创建正式 GitHub Release |
| Pull Request | 只执行构建与仓库布局检查 |

工作流同时检查仓库布局，防止世界、数据库、日志、运行沙箱和私人内容进入通用包。

## 致谢

- 感谢 [cc004/TSManager](https://github.com/cc004/TSManager) 提供旧版 TS 管理器思路。
- 感谢 [Pryaxis/TShock](https://github.com/Pryaxis/TShock) 及其贡献者维护 TShock 上游。
