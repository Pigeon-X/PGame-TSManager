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

## 独立更新工具

TShock 更新由本目录维护，不属于 PGame-TSManager 的内置功能。管理器只负责停止、启动、顺序恢复和看门狗；
更新流程单独执行，避免管理器运行时误覆盖共享 TShock 文件。

工具会先确认三服已停止，再把压缩包解到 `maintenance/staging`，校验 `TShock.Server.exe` 和
`TShockAPI.dll` 来自同一个包，并在预览模式列出待替换文件。实际替换时使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\maintenance\tools\Invoke-TShockUpdate.ps1 `
  -ManagerDir . -PackagePath .\官方TShock.zip -Apply
```

旧文件会备份到 `Backups\tshock-update-时间戳`。`-Apply` 只替换核心运行时文件，
不会修改各服 `1.PigeonServers` 下的配置、世界、数据库和插件清单。

## 标准远程更新流程

1. 在 Git 上提交本次 TShock 兼容修改，等待 Actions 构建通过。
2. 从官方 Releases 下载正式版；没有正式版时，从官方 Actions 选择明确的构建号。
3. 先执行不带 `-Apply` 的预览，确认包内版本和待替换文件。
4. 在远程源测试端停止 TSM 和三服，复制配置、插件目录、世界和数据库备份。
5. 执行 `Invoke-TShockUpdate.ps1 -Apply`，记录版本号、Actions 构建号和备份目录。
6. 重新应用 `config-translation/TransferPatch.json`，只翻译 `config.json`；REST 令牌必须为「用户名 / 用户组」。
7. 保持 `sscconfig.json` 英文，确认 `/help` 注册、描述、别名和行为未变化。
8. 运行 `--selfcheck` 和 `Test-TShockConfig.ps1`，再通过 TSM 按流光城、泰拉大陆、流光神域顺序启动。
9. 检查游戏端口 `2021/2023/2024`、REST 端口 `7878/7879/7880`、插件初始化日志和玩家指令。
10. 测试通过后才同步桌面镜像，并用 `【流光统一上报】` 格式上报。

## 回滚

停止 TSM 和三服，将 `Backups\tshock-update-时间戳` 中对应文件恢复到管理器运行目录，
再按顺序启动并重新执行全部自检。若是汉化映射造成问题，只恢复 `tshock\config.json`，不要修改 SSC。

更新流程不会把运行服务器、世界、数据库、日志或 `_runtime` 写入 Git 仓库。

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
