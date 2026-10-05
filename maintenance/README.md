# PGame-TSManager Maintenance

| 子目录 | 职责 |
| --- | --- |
| tshock-update | TShock 新版本适配、补丁、回归记录 |
| config-translation | TShock config 中文映射（含 TransferPatch.json 权威映射表） |
| rest-fixes | REST 配置和令牌字段修复（用户名 / 用户组） |
| plugin-maintenance | 独立插件维护入口，指向 PigeonPlugins |
| ssc-lock | SSC 英文配置冻结 |
| help-lock | /help 指令冻结 |
| tools | 维护工具（Test-TShockConfig.ps1 只读校验） |

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
