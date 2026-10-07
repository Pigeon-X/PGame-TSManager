# 维护工具

通用包中的工具按四目录结构工作：

```text
Core/       TShock 运行时和依赖
Servers/    每服配置和世界
Plugins/    插件总库
Tools/      维护脚本
```

## TShock 模板

`Add-TShockTemplates.ps1` 会创建两套通用模板：

| 模板 | 游戏端口 | REST 端口 |
| --- | ---: | ---: |
| `1.生存` | 7777 | 7878 |
| `2.生存2` | 7778 | 7879 |

两套模板默认必需启用 `TShockAPI.dll` 和 `HotReload.dll`。

```powershell
powershell -ExecutionPolicy Bypass -File .\Add-TShockTemplates.ps1 `
  -ManagerDir "D:\PGame-TSManager"
```

## 新建服务器

`New-PigeonServer.ps1` 会按当前四目录结构创建新服务器目录：

- 自动计算 `Servers\Profiles\序号.名字`
- 复制模板服的 `tshock\` 配置
- 写入该服的 PGame-TSManager `config.json`
- 提示世界、REST 和数据库需要修改的位置

```powershell
powershell -ExecutionPolicy Bypass -File .\New-PigeonServer.ps1 `
  -Root "D:\PGame-TSManager" `
  -Name "生存3" `
  -Port 7779 `
  -RestPort 7880
```

## 配置校验

`Test-TShockConfig.ps1` 只读检查：

1. `tshock\config.json` 使用中文键
2. REST 令牌字段使用「用户名 / 用户组」
3. `sscconfig.json` 保持英文

```powershell
powershell -ExecutionPolicy Bypass -File .\Test-TShockConfig.ps1 `
  -ServerPath "D:\PGame-TSManager\Servers\Profiles\1.生存"
```

退出码：`0` = 通过，`1` = 有失败项。

## 构建工具

| 脚本 | 用途 |
| --- | --- |
| `New-ReleaseBundle.ps1` | 生成管理器模板包 |
| `New-UniversalBundle.ps1` | 生成通用管理器包 |
| `New-SourceBundle.ps1` | 生成源码维护包 |
| `Add-TShockTemplates.ps1` | 写入 `生存`、`生存2` 两套模板 |
| `Migrate-ManagerLayout.ps1` | 迁移旧目录到 `Core / Servers / Plugins / Tools` |
| `Invoke-TShockUpdate.ps1` | 独立 TShock 更新流程 |
| `Prepare-TShockRuntime.ps1` | 下载 TShock 运行时并覆盖汉化 `TShockAPI.dll`、`HotReload.dll` |

## 重要规则

- 不修改 `/help`
- SSC 保持英文
- REST 令牌字段固定为「用户名 / 用户组」
- 私人服务器配置、RPG 插件、世界和数据库不进入通用包
