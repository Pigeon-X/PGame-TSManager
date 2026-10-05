# PGame-TSManager 维护日志

- 2026-10-05：建立 Pigeon-X TSManager 维护仓库；内联 TerrariaServerAPI；建立 TShock 更新、配置中文、REST、插件维护、SSC 和 /help 冻结目录。
- 2026-10-05：更名 TSManager -> PGame-TSManager（源码目录、解决方案、程序集均为 PGame-TSManager）；命名空间统一为 PGameTSManager；新增 serverProfiles 多服务器映射，直接接管既有服务端目录，不覆盖任何配置；启动前自动备份 server.properties / tshock\config.json / tshock\sscconfig.json。- 2026-10-05：新增 --selfcheck 无界面自检（写 selfcheck.txt，退出码 0/1）；config.json 与相对路径统一以程序目录为基准；本地与远程三个服务器（流光城 / 泰拉大陆 / 流光神域）部署完成并自检通过。
