# PGame-TSManager

Multi-server manager for TShock（鸽子服 TShock 多开管理器）。

Pigeon-X 维护版，汇总 TShock 更新、配置中文映射、REST 修复和插件维护。

## 目录

- PGame-TSManager/：管理器源码（构建产物为 PGame-TSManager.exe）。
- TerrariaServerAPI/：内联的上游子模块源码，替代 git submodule。
- maintenance/：TShock 维护分类。

## 固定规则

- TShock 更新：maintenance/tshock-update，独立于 PGame-TSManager 程序运行。
- 配置中文映射：maintenance/config-translation。
- REST 修复：maintenance/rest-fixes。
- 插件维护：maintenance/plugin-maintenance。
- SSC 英文冻结：maintenance/ssc-lock，不翻译、不改键、不改字段。
- /help 冻结：maintenance/help-lock，不修改、不禁用、不覆盖该指令。

## 构建

```powershell
dotnet build PGame-TSManager.sln -c Release
```

产物：PGame-TSManager\bin\Release\net9.0-windows\PGame-TSManager.exe。

- 目标框架 net9.0-windows，需要 .NET 9 Desktop Runtime（或发布 self-contained）。
- 本仓库只保存源码，不提交 OTAPI/XNA、TerrariaServerAPI 编译产物或其它 DLL/EXE。

### GitHub Actions 自动构建

`.github/workflows/build.yml` 会在 `main`、Pull Request 和 `v*` 标签上构建，并生成：

- `PGame-TSManager-source.zip`：源码与维护工具；
- `PGame-TSManager-template.zip`：自包含管理器和空配置模板。

流水线运行 `Test-RepositoryLayout.ps1`。`1.PigeonServers`、`_runtime`、`Worlds`、`Plugins`、
`ServerPlugins`、数据库、日志、世界文件、DLL/EXE 都不会进入源码包或 Git 跟踪。

TShock 更新不从 TSM 界面触发。请按 `maintenance/tshock-update/README.md` 的独立流程执行，
完成远程测试和回滚备份后，再使用 TSM 启动三服。

## 多服务器映射（serverProfiles）

config.json 中的 serverProfiles 直接指向已经存在的服务端目录，
PGame-TSManager 只负责 启动 / 停止 / 转发控制台，**不生成、不覆盖任何服务器配置**。

示例：

```json
{
  "backupBeforeStart": true,
  "backupDir": "Backups",
  "backupKeep": 10,
  "serverProfiles": [
    {
      "name": "流光城",
      "rootPath": "D:\\59934\\Desktop\\流光服\\1.流光城",
      "executable": "TShock.Server.exe",
      "arguments": "-lang 7 -port 2021 -world \"地图\\1.PigeonGame.wld\"",
      "enabled": true,
      "remark": "流光城 端口 2021 / REST 7878"
    }
  ]
}
```

- name：界面显示名。
- rootPath：已存在的服务端目录。
- executable：可执行文件名，默认 TShock.Server.exe。
- arguments：启动参数，原样透传给子进程。
- enabled：是否启用。
- backupBeforeStart：启动前把 server.properties、tshock\config.json、tshock\sscconfig.json
  备份到 Backups\<服务器名>\<时间戳>\，仅备份，不改写源文件。

未配置 serverProfiles 时，回退到旧版 Servers\<名称> 自建目录模式。

## TShock 6.2 兼容启动（旧模式）

旧模式（Servers\<名称>）仍可用 ManagerConfig：

- serverExecutable：默认 TShock.Server.exe。
- serverPropertiesFile：默认 server.properties。
- useTShockLaunchArguments：默认 true，使用 TShock 6.2 启动参数。

TerrariaServerAPI 源码保留在仓库中作为参考，但已从解决方案构建链移除，
避免旧 OTAPI/XNA 引用阻断 PGame-TSManager。
## 自检（一键部署后验证）

```powershell
PGame-TSManager.exe --selfcheck
```

无界面运行，检查 config.json 与每台服务器的目录 / TShock.Server.exe / tshock\config.json，
结果写入同目录 selfcheck.txt，退出码 0=全部就绪、1=有缺失。

- 程序以自身所在目录为基准解析 config.json 与相对路径（不受工作目录影响）。
- 自检只读，不会启动服务器、不会改写任何配置。
## 维护校验

```powershell
powershell -ExecutionPolicy Bypass -File maintenance\tools\Test-TShockConfig.ps1 -ServerPath '<服务端目录>'
```

只读校验该服务端目录：config.json 中文键、REST 令牌字段（用户名/用户组）、SSC 保持英文。
退出码 0 = 通过。详见 maintenance/README.md。
## 目录结构（旧版 TS 管理器模型：总库 + 每服独立插件）

```
PGame-TSManager\                          ← 运行时只此一份
├─ TShock.Server.exe  bin\ i18n\ runtimes\ x64\  GeoIP.dat   运行时母本
├─ ServerPlugins\                        ★ 插件总库（统一维护的唯一来源）
├─ PGame-TSManager.exe / config.json
├─ maintenance\  Backups\
└─ 1.PigeonServers\
    ├─ 流光城\
    │   ├─ config.json        ★ 本服 TSM 配置：启动参数 + 加载哪些插件（含 TShockAPI）
    │   ├─ ServerPlugins\     本服实际加载的插件（启动时按 config.json 从总库同步）
    │   ├─ ServerPlugins.disabled\  被停用的插件（移动，不删除）
    │   ├─ TShock.Server.exe  硬链接 → 根（不占额外空间）
    │   ├─ bin\ i18n\ runtimes\ x64\  目录联接 → 根（不占额外空间）
    │   ├─ server.properties / tshock\ / 地图\ / Logs\
    ├─ 泰拉大陆\   同上
    └─ 流光神域\   同上
```

**核心规则**

- 根 `ServerPlugins` = **插件总库**，只方便统一维护（换版本、加插件只改这里）。
- **每个服加载哪些插件，完全由该服 `config.json` 的「插件」清单决定**（绝对以 config 为准）。
- 管理器启动某服前，按该服 `config.json` 把总库里的插件同步到该服 `ServerPlugins`；
  未列出的插件移入 `ServerPlugins.disabled`（不删除，可恢复）。
- 运行时（exe/bin/i18n/runtimes/x64/GeoIP.dat）只存根目录一份，各服用
  **硬链接 / 目录联接**引用，不重复占空间。

每服 `config.json` 示例：

```json
{
  "服务器名称": "流光城",
  "启用": true,
  "启动参数": "-lang 7 -port 2021 -world \"地图\\1.PigeonGame.wld\"",
  "总插件库": "..\\..\\ServerPlugins",
  "覆盖插件目录": true,
  "插件": ["TShockAPI.dll", "AntiCheatingTool.dll", "Chameleon.dll", "..."],
  "备注": "流光城 端口 2021 / REST 7878"
}
```

## 维护方针（以 PGame-TSManager 为主）

- **唯一主体**：`PGame-TSManager` 就是这套 TShock 的运行目录，三个服都在它里面开。
- **TShock 更新**：从官方 git / Actions 取新版，由本仓库负责**兼容适配**、
  **配置汉化映射**、**config / REST 修复**。
- **SSC 保持英文**：不翻译、不改键、不改字段。
- **其余不做改动**：不动 /help，不动玩法逻辑。
- **插件更新**：所有 TShock 插件编译好后**统一放进 `Plugins\`**（插件总库）。
  哪个服用哪些插件，只改 `1.PigeonServers\<服>\config.json` 的「插件」清单。
- **插件依赖库放 `bin\`**：`Plugins\` 里只允许放 TShock 插件本体。
  不含 `TShockAPI` / `TerrariaPlugin` 引用的 DLL（如 linq2db、Microsoft.Data.Sqlite、
  SQLitePCLRaw.*、Mono.Cecil.*、MonoMod.*）属于**插件的依赖**，一律放 `bin\`。

```powershell
# 自动把 Plugins\ 里的依赖库归类到 bin\（并同步修正各服插件清单）
powershell -ExecutionPolicy Bypass -File maintenance\tools\Sort-PluginDependencies.ps1 `
  -ManagerDir D:\59934\Desktop\PGame-TSManager

# 清理残余与备份（归档/Backups/沙箱日志/停用插件/生成物）
powershell -ExecutionPolicy Bypass -File maintenance\tools\Remove-UnrelatedFiles.ps1 `
  -ManagerDir D:\59934\Desktop\PGame-TSManager
```

## 目录里的几个特殊文件夹

| 目录 | 是什么 | 能不能删 |
| --- | --- | --- |
| `bin\` | TShock 运行时本体（OTAPI/TerrariaServer/ModFramework/HttpServer）+ **插件的依赖库**（linq2db、Microsoft.Data.Sqlite、SQLitePCLRaw.*、Mono.Cecil.*、MonoMod.*） | ❌ 不能删 |
| `runtimes\` | 插件依赖带的**原生库目录**：SQLite 原生引擎 `e_sqlite3`，按平台各一份。Windows 只需 `win-x64\`，其余是死重量 | 只删非 win-* 平台即可 |
| `x64\` | TShock 自己建的空目录 | 空的可删 |
| `_runtime\<服>\` | 每服的**运行沙箱**（exe 硬链接、bin/i18n/runtimes 用目录联接、tshock 联接回真配置目录、ServerPlugins 按该服 config.json 同步）。程序自动生成 | 可删，下次启动重建 |
| `Plugins\` | 插件总库，**只放 TShock 插件本体** | ❌ 不能删 |
| `Worlds\` | 世界文件 | ❌ 不能删 |

```powershell
# 精简 runtimes（只留 Windows 平台）
powershell -ExecutionPolicy Bypass -File maintenance\tools\Trim-Runtimes.ps1 -ManagerDir D:\59934\Desktop\PGame-TSManager
```

## 运行方式

- 默认 **只有 PGame-TSManager 一个窗口**：服务器用隐藏控制台运行（保住 stdin 不会 EOF 自退），
  输出写进 `_runtime\<服>\console.log` 由管理器面板显示。
- 管理器输入框发指令走该服的 **REST**（`/v2/server/rawcmd`），不需要控制台。
- 想给某台服务器单独开可见控制台：把 `config.json` 的 `showServerWindow` 改成 `true`。
- 调试可用命令行 `--nowindow`（强制无窗口）配合 `--startall`。

## 启动方式

**直接双击 exe 启动，不再用 bat：**

- 双击桌面快捷方式「PGame-TSManager 管理器」
- 或双击 `PGame-TSManager.exe`

**三个服务器都在 PGame-TSManager 里开启**：

- 顶部下拉框选服务器，右上角点「启动本服 / 停止本服」
- 右上角点「**全部启动**」/「全部停止」（三服一键开关）
- 插件开关、新建服务器、刷新列表、保存世界和广播在顶部菜单栏中

服务器在管理器内部运行，日志显示在管理器的控制台面板，输入框可在管理器里发指令。
`config.json` 的 `showServerWindow`（默认 `false`）改成 `true` 时，才会另开独立控制台窗口。

命令行也可一键开三服（打开管理器并自动启动全部）：

```powershell
PGame-TSManager.exe --startall
```

图标：`PGame-TSManager.ico`（16/24/32/48/64/128/256）已嵌入 exe，同时作为程序与窗口图标。

## 插件同步

启动服务器前自动执行；也可单独执行（不启动服务器）：

```powershell
PGame-TSManager.exe --syncplugins
```

结果写入 `syncplugins.txt`。

## 建/修布局脚本

```powershell
# 建立“总库 + 每服独立插件 + 共享运行时引用”
powershell -ExecutionPolicy Bypass -File maintenance\tools\To-PerServerPluginsLayout.ps1 `
  -ManagerDir D:\59934\Desktop\PGame-TSManager

# 迁移三服到 1.PigeonServers
powershell -ExecutionPolicy Bypass -File maintenance\tools\New-PigeonServersLayout.ps1 `
  -ManagerDir D:\59934\Desktop\PGame-TSManager -SourceRoot D:\59934\Desktop\流光服
```

## 实测结论（TShock 6.2.1）

- TShock 的插件目录 = **TShock.Server.exe 所在目录**（子目录里的 ServerPlugins 不生效）。
  所以要让各服插件独立，每个服必须有它自己的 exe（本项目用硬链接，不占空间）。
- 工作目录 = 各服目录 ⇒ tshock 配置、世界、日志、插件数据各归各的。
