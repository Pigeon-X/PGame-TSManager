# TSManager

Multi-server manager for TShock.

Pigeon-X 维护版，汇总 TShock 更新、配置中文映射、REST 修复和插件维护。

## 目录

- TSManager/：管理器源码。
- TerrariaServerAPI/：内联的上游子模块源码，替代 git submodule。
- maintenance/：TShock 维护分类。

## 固定规则

- TShock 更新：maintenance/tshock-update。
- 配置中文映射：maintenance/config-translation。
- REST 修复：maintenance/rest-fixes。
- 插件维护：maintenance/plugin-maintenance。
- SSC 英文冻结：maintenance/ssc-lock。
- /help 冻结：maintenance/help-lock，不修改该指令。

## 构建前置

- 本仓库只保存源码，不提交 OTAPI、XNA、TerrariaServerAPI 编译产物或其它 DLL。
- 构建 TSManager 前需在外部准备对应版本的 OTAPI/XNA/TShield 参考程序集。
- 缺少这些参考程序集时，dotnet build 会出现 OTAPI/XNA 类型缺失错误，不代表源码结构损坏。
