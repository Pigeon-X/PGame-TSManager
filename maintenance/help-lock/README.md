# /help 指令冻结规则

禁止修改 /help 的注册、HelpText、别名或执行行为。

- TShock 更新时只确认 /help 仍然可用。
- 插件不得注释、覆盖或删除 /help。
- 如需帮助增强，使用独立 HelpPlus（本项目不接管 /help 本体）。
- 历史问题：HelpPlus 曾把 /help 指令注释掉导致玩家看不到指令，修复后保持独立。

## 检查清单

- [ ] 更新后玩家输入 /help 能正常显示指令列表。
- [ ] 没有任何插件注册同名 /help 或做别名覆盖。
- [ ] 未改动 TShock 内置 HelpText 资源。