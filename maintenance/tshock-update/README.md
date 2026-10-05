# TShock 更新维护

用途：跟进官方 TShock 新版本，整理更新覆盖、汉化映射、配置迁移和回归验证。

## 更新来源

- 正式版：https://github.com/Pryaxis/TShock
- 测试版（最新构建）：https://github.com/Pryaxis/TShock/actions

优先用正式版；正式版未出时从 Actions 取最新可用构建。

## 更新步骤

1. 备份当前服务端目录（`TShock.Server.exe`、`ServerPlugins\`、`tshock\config.json`、`tshock\sscconfig.json`）。
2. 从上面的来源取新版本，替换 `TShock.Server.exe` 与 `ServerPlugins\`。
3. **重新应用汉化**：官方 `TShockAPI.dll` 被替换后，用 `ApplyTransferPatch`
   按 ../config-translation/TransferPatch.json 重新改写 JSON 属性名。
4. **校验**：

```powershell
powershell -ExecutionPolicy Bypass -File ..\tools\Test-TShockConfig.ps1 -ServerPath '<服务端目录>'
```

5. 确认 /help 正常（见 ../help-lock），SSC 仍为英文（见 ../ssc-lock）。
6. 记录版本号、提交号与上报回执到 change-log。

## 规则

- 只修改 TShock 更新相关文件。
- 不修改 /help 注册、描述、别名或行为。
- SSC 配置保持英文，不改键、不翻译。
- REST 修复记录在 ../rest-fixes。
- 配置中文映射记录在 ../config-translation。

## 构建前置

- 本仓库只保存源码，不提交 OTAPI、XNA、TerrariaServerAPI 编译产物或其它 DLL。
- 构建 PGame-TSManager 前需在外部准备对应版本的 OTAPI/XNA/TShield 参考程序集。
- 缺少这些参考程序集时，dotnet build 会出现 OTAPI/XNA 类型缺失错误，不代表源码结构损坏。

## 版本记录

### 2026-10-05 现代启动适配

- PGame-TSManager 目标框架升级到 net9.0-windows。
- 默认使用 TShock.Server.exe 与启动参数（不用旧 TerrariaServerAPI）。
- 三服（流光城 / 泰拉大陆 / 流光神域）经 PGame-TSManager 接管，自检 3/3 通过。