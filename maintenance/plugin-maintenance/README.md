# 插件维护

独立 TShock / TSL 插件源码统一维护在：

https://github.com/Pigeon-X/PigeonPlugins

规则：

- TShock 独立插件：PigeonPlugins/tshock。
- TSL 独立插件：PigeonPlugins/tsl。
- Bot 耦合插件：PGameAPI 对应目录。
- Dimensions 核心耦合插件：PigeonDimension 对应目录。
- 本目录只记录 TShock 更新时的插件兼容结论，不复制插件源码。

## 现状（2026-10-05）

- 三服共用的独立插件（流光系统 / PlayerReward / ProgressBag / HelpPlus 等）走 PigeonPlugins。
- CustomPlayer、StatusTextManager 已移到 PGameAPI。
- CGive 保留在两个 RPG 服（流光神域 / 泰拉大陆）。
- 每次 TShock 换版后，逐个确认插件在 ServerLog 中无 unhandled exception（尤其 GameInitialize 阶段）。

## 兼容检查清单

- [ ] 服务器启动日志无插件异常（Unhandled exception / 初始化失败）。
- [ ] 数据库连接正常（CGive / CustomPlayer / PigeonRPG 等）。
- [ ] /help 未被插件覆盖。
- [ ] 插件配置键未被误翻译（除 TShock config 与已约定项）。