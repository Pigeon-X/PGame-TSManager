# 对接回执｜请 RPG(PigeonRPG.ProgressGuard) 支持「世界阶段自动化」

> **收件方**：PigeonRPG / RPG 项目会话
> **发起方**：TSM / 生存（鸽子直播服运维侧）
> **日期**：2026-10-09
> **优先级**：中（当前不阻塞，但直播服打完肉山后必须人工改配置，否则卡困难门口）

---

## 一、背景

鸽子直播服（serverId=0）已按「单服完整进度」配好：

- `ProgressControl.json`（计划书）：肉前时段取泰拉大陆、困难时段取流光神域，**合并成一张 Boss 解锁表**；
- `ProgressGuard.json`：`阻止困难化 = false` → **允许世界进入困难模式**；
- `ProgressSync.json`：同步模式全 `2`（本服单机，不外传）。

**问题**：`ProgressGuard` 的「世界阶段」是**静态配置**，插件不会跟着世界实际状态走。
肉山被打死、世界真正进入困难后，插件仍按「肉前世界」判定 → **困难 Boss / 困难物品仍被当超进度拦下**，
必须人工改 `ProgressGuard.json` 再重载/重启。这对直播服不可接受。

---

## 二、现状（代码事实）

| 位置 | 行为 |
|---|---|
| `PigeonRPG.ProgressGuard/Config.cs:15-16` | `世界阶段`（`WorldStage`）默认 `肉前世界`，纯配置 |
| `PigeonRPG.ProgressGuard/Plugin.cs:770-774` | `IsPreHardmodeWorld()` **只读配置**，不看 `Main.hardMode`；注释明确「泰拉大陆按配置固定锁肉前……不能自动放行」 |
| `Plugin.cs:827-850` | `ShouldBlockBoss()`：肉前→拦困难 Boss；困难→拦肉前 Boss |
| `Plugin.cs:881-884` | `ShouldBlockHardmode()` = `阻止困难化 && 肉前世界` |
| `Plugin.cs:182 / 190 / 306` | 超进度物品/弹幕/状态、困难事件限制，全部以 `IsPreHardmodeWorld()` 为开关 |
| `Plugin.cs:855-861` | `OnGamePostInitialize`：`强制困难模式` 时 `Main.hardMode = true` |
| `Plugin.cs:863-879` | `StartHardmode` / `initializeHardMode` 两个 Hook，目前只用于「拦截」 |

> 现状是**故意**不自动（为了让泰拉=肉前服、神域=困难服 的分服设计稳定）。所以这次要的是**可选开关**，不是改默认。

---

## 三、诉求：新增「自动跟随世界阶段」开关

### 建议配置项（默认关，保证兼容）

```jsonc
// PigeonRPG.ProgressGuard.json
"自动跟随世界阶段": false,   // 新增；false=维持现状（默认），true=跟随 Main.hardMode
```

### 行为（仅当 `自动跟随世界阶段 = true`）

```
有效阶段 = Main.hardMode ? "困难世界" : "肉前世界"
```

生效点建议：

1. `OnGamePostInitialize`（`Plugin.cs:855`）—— 世界加载后同步一次；
2. `OnStartHardmode` / `OnInitializeHardMode`（`Plugin.cs:863/872`）—— 不拦时，同步切到 `困难世界`；
3. `OnGameUpdate` 低频兜底（复用现有 `_updateFrame % 600`，`Plugin.cs:182`）—— 处理「世界已是困难但没触发 Hook」的场景。

切换时输出一行日志：

```
[PigeonRPG.ProgressGuard] 世界阶段自动切换：肉前世界 -> 困难世界
```

### 边界要求（很重要）

1. **默认 `false`，泰拉大陆(2) / 流光神域(3) 行为必须零变化**（它们的分服设计依赖手动固定阶段）。
2. `世界阶段` 仍是手动覆盖入口；`自动跟随世界阶段=false` 时完全按老逻辑走。
3. 自动模式下 `ShouldBlockHardmode()` 不应因为阶段自动切换而**反而拦掉困难化**——即自动模式与 `阻止困难化=false` 搭配使用；若 `阻止困难化=true` 且自动跟随，请以「阻止困难化优先」并在日志说明。
4. 切阶段**不要写成多世界互相覆盖的全局值**：建议运行期生效、不写盘；若要持久化，请按世界唯一 ID 存（参考 `WorldFileData.UniqueId`）。
5. 任何异常都要 try/catch，绝不能影响世界加载 / 卡 tick。
6. `Main.hardMode` 在 `GamePostInitialize` 早期可能仍是 `false`，必须有 tick 兜底。

---

## 四、验收清单（请逐条回报）

- [ ] 新增 `自动跟随世界阶段`（bool，默认 **false**），配置迁移不破坏老文件
- [ ] `false` 时：泰拉(肉前) / 神域(困难) 判定与今天**逐条一致**
- [ ] `true` + 肉前世界：`IsPreHardmodeWorld()==true`，困难 Boss 被拦
- [ ] `true` + `Main.hardMode==true`：**自动切「困难世界」**，困难 Boss 放行、肉前 Boss 被拦
- [ ] `true` + 服务器重启（世界已是困难）：自动识别为困难，**无需人工改配置**
- [ ] 切换有日志；`/pgreload` 后开关仍生效
- [ ] `强制困难模式=true` 时与自动跟随不冲突
- [ ] 超进度物品/弹幕/状态、困难事件限制在新阶段下判定正确
- [ ] 异常不抛到世界加载流程

---

## 五、直播服当前配置（供研发对拍）

```jsonc
// Servers\Profiles\0.鸽子直播服\tshock\PigeonRPG\PigeonRPG.ProgressGuard.json
{
  "配置版本": 5,
  "世界阶段": "肉前世界",
  "阻止困难化": false,        // ← 已放开困难
  "强制困难模式": false,
  "启用Boss限制": true,
  "启用超进度限制": true,
  "启用困难事件限制": true
  // 期望新增： "自动跟随世界阶段": true
}
```

> 直播服**没有**加载 Runtime / Equipment / MonsterTier / Skill（精简方案），ProgressGuard 请保持「只依赖共享表 `rpg_account_progress`」的边界，不要引入对上述模块的硬依赖。

---

## 六、模板回执（请照此填回即可）

```
【RPG → TSM/生存】ProgressGuard 世界阶段自动化 回执
- 是否已实现「自动跟随世界阶段」：是/否
- 配置项名称/默认值：
- 生效点（PostInit / StartHardmode / GameUpdate）：
- 是否持久化（运行期 / 按世界ID）：
- 与「阻止困难化」「强制困难模式」的优先级：
- 自测环境（服/端口/世界）：
- 自测结果：
  1) false 兼容：
  2) true 肉前：
  3) true 切困难：
  4) 重启识别：
  5) /pgreload：
- 新 DLL SHA256：
- 是否有额外约束/注意：
```
