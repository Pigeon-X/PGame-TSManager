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