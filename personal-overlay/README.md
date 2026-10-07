# Pigeon Personal Overlay

这个目录只描述个人版叠加层，不在公共 Core 包里分发实际个人文件。

个人版结构建议：

```text
personal-overlay/
├─ overlay.json
├─ plugins/        # 个人 TShock 插件，例如 PigeonRPG、PGameAPI、PChrome
├─ bin/            # 这些插件自己的依赖库
├─ data/           # 个人数据、缓存、映射表
├─ servers/        # 1.PigeonServers 个人服配置清单
├─ rpgConfigs/     # RPG 独立配置
├─ botIntegration/ # PigeonBot / 机器人耦合文件
└─ config/
   └─ manager.json # 个人 TSM 覆盖配置
```

构建规则：

1. Core 仓库只构建通用管理器、TShock 适配、通用 UI 和公共插件。
2. Personal 仓库从同一个 Core tag/commit 构建，再执行 `Apply-PersonalOverlay.ps1`。
3. Personal 包永远不把个人插件、PGameAPI、PigeonRPG、机器人强耦合文件合并进 Core 包。
4. Core 与 Personal 共用版本号；Personal 构建记录 `coreCommit` 和 `personalOverlayCommit`。
5. 公共仓库的 Actions 只上传 Universal artifact；Personal artifact 由私有仓库或私有 overlay 流水线上传。
