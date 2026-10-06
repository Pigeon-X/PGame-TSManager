# PGame-TSManager 维护日志

- 2026-10-05：建立 Pigeon-X TSManager 维护仓库；内联 TerrariaServerAPI；建立 TShock 更新、配置中文、REST、插件维护、SSC 和 /help 冻结目录。
- 2026-10-05：更名 TSManager -> PGame-TSManager（源码目录、解决方案、程序集均为 PGame-TSManager）；命名空间统一为 PGameTSManager；新增 serverProfiles 多服务器映射，直接接管既有服务端目录，不覆盖任何配置；启动前自动备份 server.properties / tshock\config.json / tshock\sscconfig.json。- 2026-10-05：新增 --selfcheck 无界面自检（写 selfcheck.txt，退出码 0/1）；config.json 与相对路径统一以程序目录为基准；本地与远程三个服务器（流光城 / 泰拉大陆 / 流光神域）部署完成并自检通过。
- 2026-10-05：充实 maintenance 维护资产。config-translation 导入权威映射表 TransferPatch.json（启用=True，只含 TShockSettings 146 + TokenData 2，不含 SscSettings）；新增 tools/Test-TShockConfig.ps1 只读校验（中文键 / REST 用户名·用户组 / SSC 英文）；rest-fixes、ssc-lock、help-lock、tshock-update、plugin-maintenance 补充规则与流程；本地三服校验 3/3 通过。- 2026-10-05：三服归类到 PGame-TSManager（Desktop），新增「总插件库」Plugins\ 与 1.PigeonServers\；每服 config.json 声明启动参数与启用插件；新增 PluginSync（启动前按清单同步 ServerPlugins，未启用项移入 ServerPlugins.disabled）与 --syncplugins 命令；管理器界面与日志改中文；新增 maintenance\tools\New-PigeonServersLayout.ps1 迁移脚本。
- 2026-10-05：PGame-TSManager 改为「一个 TShock」：运行时（exe/bin/i18n/runtimes/x64/GeoIP.dat）与 ServerPlugins 只存根目录一份，各服只留 config.json+server.properties+tshock+世界+Logs；新增 To-SingleTShockLayout.ps1；管理器新增 useSharedRuntime。实测 TShock 插件目录固定=exe 目录，故一 exe 必然一套插件。
- 2026-10-05：按旧版 TS 管理器模型改造：根 ServerPlugins = 插件总库（统一维护）；每服 config.json 的「插件」清单决定该服加载哪些插件（含 TShockAPI）；每服 ServerPlugins 由管理器按 config.json 从总库同步，未列出的移入 ServerPlugins.disabled；运行时用硬链接/目录联接共享，不重复占空间。新增 To-PerServerPluginsLayout.ps1。远程三服已验证：流光城 16 个插件（无 CGive/PigeonRPG），泰拉大陆/流光神域各 29 个（含 CGive/PigeonRPG）。
- 2026-10-05：新增程序图标（PGame-TSManager.ico，16-256 多尺寸，嵌入 exe 并作为窗口图标）；新增 showServerWindow（默认 true，每服启动开独立可见控制台窗口）；新增 Remove-UnrelatedFiles.ps1 清理脚本；本地清出 340MB、远程清出 390MB；启动改为双击 exe / 桌面快捷方式，移除所有 .bat/.cmd 启动方式；远程三服已在 RDP 会话（Session 2）以可见窗口重启。
- 2026-10-05：管理器改为「三服都在 PGame-TSManager 里开启」：新增「全部启动 / 全部停止」按钮与 --startall 命令行（打开窗口并自动启动三服）；showServerWindow 默认改回 false（服务器在管理器内运行，输出在管理器控制台面板）。本地+远程均已部署，远程管理器与三服均在 RDP 会话（Session 2）可见运行。
- 2026-10-05：确立维护方针——以 PGame-TSManager 为唯一主体；TShock 更新由本仓库做兼容/汉化映射/config-REST 修复，SSC 保留英文，其余不动；插件编译好统一入 Plugins\，插件依赖库（不含 TShockAPI/TerrariaPlugin 引用）归入 bin\；各服启停由 1.PigeonServers\<服>\config.json 决定。新增 Sort-PluginDependencies.ps1；扩展 Remove-UnrelatedFiles.ps1（清归档/Backups/沙箱日志/停用插件/生成物）。本地依赖 5 个、远程 15 个已归入 bin\。
- 2026-10-05：明确 _runtime 用途（每服运行沙箱：exe 硬链接 + bin/i18n/runtimes 目录联接 + tshock 联接回真配置 + 该服 ServerPlugins）并写进 README；启动改为「只有 PGame-TSManager 一个窗口」——服务器用隐藏控制台（stdin 不会 EOF 自退）+ 输出重定向到 console.log 由管理器回显，发指令改走各服 REST rawcmd；新增 --nowindow 调试开关；新增 Trim-Runtimes.ps1，runtimes 只保留 Windows 平台（28.5MB -> 5.7MB，本地+远程各省 23MB）。
- 2026-10-05：锁定两条硬规则——① PGame-TSManager.exe 启动必须可视化显示窗口（WindowState=Normal、ShowInTaskbar=True、Activate+置前后台），不允许隐藏/无窗口；② 绝不带出 TShock 控制台窗口，服务器一律隐藏控制台运行（用 SW_HIDE 直启 exe，保留真实控制台以免 stdin EOF 自退）。面板回显改为读 TShock 自己写的 _runtime\<服>\Logs\<日期>.log。远程服务器定位为「源测试端」。

- 2026-10-06：最小化/关闭交互定稿（选项 B 落地）——点 × 弹「缩小到托盘 / 关闭程序 / 取消」，选「关闭」再弹一次是与否确认；新增 _exiting 放行标志，修掉确认退出时 Current.Shutdown() 再次触发 Closing 可能二次弹窗的问题；托盘菜单「退出」同样直接放行。远程三服复测：TSM 窗口 Visible、TShock 窗口 0、2021/2023/2024 + REST 7878/7879/7880 全部 200、插件 15/29/29 无异常。

- 2026-10-06：**发指令链路打通**。查官方 TShock 6.2.1 源码（TShockAPI\Rest\RestManager.cs）发现 rawcmd 端点是 /v3/server/rawcmd，旧的 /v2/server/rawcmd 必然 404 —— 这就是长期发不出指令的真正原因。TSM 改为 REST 主通道（自动补前导 /，解析 response 数组，去掉 [c/xxx:] 颜色标记），删除失效的 ConsoleInjector；新增 --send 无界面发指令模式。远程三服实测 exit=0 并回显命令列表；新增 maintenance\command-channel\README.md。

- 2026-10-06：控制台彩色渲染升级。REST 指令回显不再剥掉 Terraria 的 [c/RRGGBB:文字] 颜色标记，改为按标记拆成彩色片段渲染（/help 现在显示 TShock 原生配色：粉色标题、青色斜杠、黄色指令、红色 @、蓝色说明）；无标记的行仍走原有关键字着色。

- 2026-10-06：界面改版。① 原生标题栏改深色（DwmSetWindowAttribute 20/19），Win11 上还会加圆角与紫色标题栏/描边（Server 2022 自动跳过）；② 内容区改成圆角卡片 + 顶部亮紫描边；③ 控制台滚动条换成 10px 圆角亮紫滑块（悬停更亮）；④ 输入框底色改亮（#26223A，不再是暗底）+ 聚焦时紫色描边 + 灰字占位提示；⑤ 主色由黄色改成亮紫 #A855F7（发送按钮、下拉箭头、聚焦描边、滚动条），保留绿/红启停按钮；⑥ 控制台字体加 Microsoft YaHei UI 回退。

- 2026-10-06：① 液态玻璃界面（Win10/11 通用）：窗口底色改极光渐变 + 两团光斑，主面板/按钮/下拉框/输入框全部半透明玻璃（1px 高光描边、悬停点亮）；不依赖系统模糊，RDP 下也不会掉效果。② 全部启动改顺序启动：新增 startAllSequential（默认 true）与 startReadyTimeoutSeconds（默认 240），逐台启动并等上一台真正就绪（游戏端口进入监听 + 2.5 秒稳定期）再起下一台，单台超时跳过；按钮文案改「全部启动（顺序）」。③ 界面初始化失败改为明确报错退出，不再留下无窗口的僵死进程。
