# PGame-TSManager

面向 Windows 的 TShock 多服务器管理器。用同一个管理器维护多个服务端目录，统一完成启动、停止、控制台查看、指令发送、插件同步、备份和自检。

## 主要功能

- 一个窗口管理多个 TShock 服务端，可切换服务器查看日志。
- 支持单服启动、顺序全部启动、全部停止。
- 支持服务器配置、插件清单、初始物品和 SSC 的可视化设置。
- 启动前按服务器配置同步插件，停用插件不删除。
- 支持启动前配置备份、运行日志、崩溃检测和自动重启。
- 支持 REST 指令发送、控制台显示、热重载和插件状态检查。
- 支持无界面自检，部署后可直接验证目录和配置完整性。

## 运行要求

- Windows 10 / Windows 11
- TShock 6.2.x
- 自包含发布包不需要额外安装 .NET；源码构建需要 .NET 9 SDK

## 快速开始

1. 解压部署包到目标目录。
2. 编辑根目录 `config.json`，确认 `serverProfiles` 指向正确的服务端目录。
3. 双击 `PGame-TSManager.exe` 启动管理器。
4. 在服务器列表选择目标服务器，点击“启动本服”或“全部启动”。
5. 首次部署后执行一次自检，确认服务和配置目录完整。

## 常用命令

```powershell
# 启动管理器并顺序启动全部服务器
PGame-TSManager.exe --startall

# 只同步插件，不启动服务器
PGame-TSManager.exe --syncplugins

# 无界面自检，结果写入 selfcheck.txt
PGame-TSManager.exe --selfcheck

# 强制无界面运行
PGame-TSManager.exe --nowindow
```

## 目录说明

| 目录或文件 | 用途 |
| --- | --- |
| `PGame-TSManager.exe` | 管理器主程序 |
| `config.json` | 服务器列表和运行策略 |
| `1.PigeonServers` | 各服务器独立目录 |
| `Plugins` | 插件统一维护目录 |
| `bin` | TShock 与插件依赖库 |
| `_runtime` | 运行沙箱、控制台日志和临时运行文件 |
| `Backups` | 配置备份 |
| `Data` | 中文物品名等通用数据 |
| `maintenance` | 更新、校验和维护工具 |

## 服务器配置

`config.json` 中的 `serverProfiles` 负责描述服务器。管理器只读取该配置并启动对应目录，不会自动改写服务器自身配置。

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

- `name`：管理器显示名称。
- `rootPath`：现有服务端目录。
- `executable`：服务端可执行文件。
- `arguments`：原样透传的启动参数。
- `enabled`：是否允许启动。

## 插件加载规则

- `Plugins` 是插件总库。
- 每台服务器实际加载哪些插件，以该服务器 `config.json` 的插件清单为准。
- 启动前由管理器同步到运行目录。
- 不在清单中的插件会移入停用目录，不直接删除。
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
- 通用底层改动在本仓库维护；个人服、私人插件和 RPG 数据由私有覆盖层维护。

## 构建

```powershell
dotnet build PGame-TSManager.sln -c Release

dotnet publish PGame-TSManager/PGame-TSManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/manager
```

## 自动构建

`.github/workflows/build.yml` 在 `main`、Pull Request 和 `v*` 标签触发，生成：

- `PGame-TSManager-universal.zip`
- `PGame-TSManager-template.zip`
- `PGame-TSManager-source.zip`

`main` 推送或手动运行工作流时，会自动创建
`v<项目版本>-build.<运行号>` 预发布，并把上述三个 ZIP 上传到 GitHub Releases。
推送 `v*` 标签时进入正式 Release 流程。

工作流同时执行仓库布局检查，防止世界、数据库、日志、运行沙箱和私人内容进入通用包。
