# TShock 更新维护

用途：跟进官方 TShock 新版本，整理更新覆盖、汉化映射、配置迁移和回归验证。

规则：

- 只修改 TShock 更新相关文件。
- 不修改 /help 注册、描述、别名或行为。
- SSC 配置保持英文，不改键、不翻译。
- REST 修复记录在 ../rest-fixes。
- 配置中文映射记录在 ../config-translation。

## 构建前置

- 本仓库只保存源码，不提交 OTAPI、XNA、TerrariaServerAPI 编译产物或其它 DLL。
- 构建 TSManager 前需在外部准备对应版本的 OTAPI/XNA/TShield 参考程序集。
- 缺少这些参考程序集时，dotnet build 会出现 OTAPI/XNA 类型缺失错误，不代表源码结构损坏。

## 2026-10-05 现代启动适配

- TSManager 目标框架升级到 net9.0-windows。
- 默认使用 TShock.Server.exe 和 server.properties 启动。
- 旧 TerrariaServerAPI 项目从 TSManager.sln 移除，源码保留供参考。
- 当前 TSManager.sln 构建结果为 0 警告、0 错误。
