# PGame-TSManager

Multi-server manager for TShock（鸽子服 TShock 多开管理器）。

Pigeon-X 维护版，汇总 TShock 更新、配置中文映射、REST 修复和插件维护。

## 目录

- PGame-TSManager/：管理器源码（构建产物为 PGame-TSManager.exe）。
- TerrariaServerAPI/：内联的上游子模块源码，替代 git submodule。
- maintenance/：TShock 维护分类。

## 固定规则

- TShock 更新：maintenance/tshock-update。
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
## 目录结构（PGame-TSManager = 一个 TShock）

PGame-TSManager 本身就是这套 TShock 的运行目录；三台服务器只是「三个世界 + 三份配置」。
运行时与插件只存一份，不再每个服复制。

```
PGame-TSManager\                  ← 一个 TShock（运行时只此一份）
├─ TShock.Server.exe
├─ bin\  i18n\  runtimes\  x64\  GeoIP.dat      TShock 运行依赖
├─ ServerPlugins\                              唯一插件目录（三服共用）
├─ PGame-TSManager.exe / config.json           管理器与配置
├─ maintenance\  Backups\
└─ 1.PigeonServers\
    ├─ 流光城\    config.json + server.properties + tshock\ + 地图\ + Logs\
    ├─ 泰拉大陆\  config.json + server.properties + tshock\ + world\ + Logs\
    └─ 流光神域\  config.json + server.properties + tshock\ + world\ + Logs\
```

每台服务器启动时：工作目录 = 自己的目录（tshock 配置、世界、日志、插件数据各归各的），
程序本体 = 根目录的 TShock.Server.exe（运行时与插件三服共用）。

> 实测结论：TShock 的插件目录固定等于 **TShock.Server.exe 所在目录**，
> 因此「一个 exe」必然「一套插件」。想让各服加载不同插件，就必须各留一份运行时。

每服 `config.json`（TSM 配置）：

```json
{
  "服务器名称": "流光城",
  "启用": true,
  "启动参数": "-lang 7 -port 2021 -world \"地图\\1.PigeonGame.wld\"",
  "总插件库": "",
  "覆盖插件目录": false,
  "插件": ["AntiCheatingTool.dll", "..."],
  "说明": "插件由根目录 ServerPlugins 统一提供；本清单用于自检该服插件是否齐备。"
}
```

- `启动参数`：该服启动参数（工作目录 = 本服目录）。
- `插件`：该服需要的插件清单，自检时核对是否都在共享 `ServerPlugins` 里。
- `覆盖插件目录`：共享模式下保持 `false`。

## 一键把三服合并成一个 TShock

```powershell
powershell -ExecutionPolicy Bypass -File maintenance\tools\To-SingleTShockLayout.ps1 `
  -ManagerDir D:\59934\Desktop\PGame-TSManager
```

把各服的 `TShock.Server.exe`、`bin`、`i18n`、`runtimes`、`x64`、`GeoIP.dat`、`ServerPlugins`
提升/合并到管理器根目录，各服只留配置与世界；移出的文件进 `_归档_<时间戳>\`（不删除）。
