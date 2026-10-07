# TShock 运行时覆盖文件

`TShockAPI.dll` 对应 TShock `6.2.1`，用于通用包的中文 `config.json` 兼容。
`HotReload.dll` 作为两套模板的默认热重载插件。

构建通用包时：

1. 从 Pryaxis/TShock 最新正式版下载 Windows x64 运行时。
2. 把官方运行时放入包的 `Core`。
3. 用本目录的 `TShockAPI.dll` 覆盖 `Core\ServerPlugins\TShockAPI.dll`。
4. 将 `TShockAPI.dll` 和 `HotReload.dll` 同步到 `Plugins`，供每服沙箱复制。

SSC 仍保持英文；REST 令牌字段仍使用「用户名 / 用户组」。
