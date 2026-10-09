# 鸽子直播服：WorldGen.nextCount 自递归诊断

> 时间：2026-10-09
> 范围：`serverId=0` 鸽子直播服建图 StackOverflow
> 状态：**根因未确定**（间歇性；已撤回对 ProgressLoot 的误判）。直播服暂以「大世界+大师」运行

---

## ⚠ 更正（2026-10-09，TSM 会话）：我曾误判为 ProgressLoot —— **该结论已撤回**

### 撤回说明

我先前基于**单次**沙箱运行（`ProgressLoot` 单独 → StackOverflow）判定元凶是
`PigeonRPG.ProgressLoot`，并被 PigeonRPG 会话以反证驳回（该插件只有
`NpcKilled` / `GameUpdate` / `PlayerLogout` 三个钩子，全仓库无
`nextCount`/`countTiles`/`MonoMod`/`Detour`）。**复测证实我错了**：

```text
PGameAPI 单独（中图/经典）        -> WORLD_OK  6,892,939 B  无 StackOverflow
ProgressLoot 单独（中图/经典）第 1 次 -> CRASH
ProgressLoot 单独（中图/经典）第 2 次 -> WORLD_OK  6,952,127 B  无 StackOverflow   ← 不可复现
```

⇒ 该崩溃是**间歇性**的，**不能归因于单个插件**；「改大图+大师即修复」也**未经证实**（很可能只是那次没抽到触发条件）。

### 目前可确定的（确定性证据）

1. 全插件 DLL 的 **IL 扫描 + 字符串扫描**：`nextCount` / `countTiles` / `mfwh_` /
   `orig_nextCount` / `hook_nextCount` **在 20 个插件里一处都没有** → 没有任何插件按名字 hook `nextCount`。
2. `mfwh_*` 包装只存在于 **OTAPI / ModFramework 生成层**（`Core\bin\OTAPI*.dll`、`TerrariaServer.dll`）→
   栈里的 `WorldGen.nextCount ↔ WorldGen.mfwh_nextCount` 是**该层自身**的包装递归。
3. 引用 `ModFramework` 的插件：AntiCheatingTool、CommandTool、FixTools、PGameAPI、PigeonMiniGamesAPI、TShockAPI；
   引用 `MonoMod/Detour` 的：**HotReload、PGameAPI**。与 PigeonRPG 会话的观察一致。
4. 崩溃**间歇发生**，与随机种子/时序相关（每次 `Creating world - Seed:` 都不同；崩溃点固定在某个 gen pass）。

### 尚待验证（下一步）

- 只装 `PGameAPI` 连跑 N 次，统计 `Stack overflow` 失败率；对照「不装 PGameAPI」同 N 次。
- 枚举 PGameAPI 通过 ModFramework 注册的 hook 清单（看是否 hook 了 WorldGen 相关方法、`orig` 是否串联正确）。
- PigeonRPG 会话已表示可协助 IL/Metadata 分析 —— 采纳。

### PGameAPI 钩子枚举结果（2026-10-09，Cecil）

`PGameAPI.dll` 程序集引用：`MonoMod.RuntimeDetour 25.2.3`、`ModFramework 1.1.15`、`OTAPI 1.4.5.8`、
`TerrariaServer 6.1.0.0`、`TShockAPI 6.1.0.0`。

- `On.*`（HookGen 风格）钩子目标：**0 个** —— 它**不是**用 `On.` 包装，而是直接用 `MonoMod.RuntimeDetour`。
- `MonoMod.RuntimeDetour.Hook` 字段/目标：

```text
ModFramework.ICollection`1<Terraria.ITile>      ← 触碰 tile 集合（与 tile 扫描相关）
MonoMod.RuntimeDetour.Hook  HouseRegion.HouseCore
                           TShockData.ParticleGuard
                           TShockData.ShopUICore
                           TShockData.BossLimitSummon
                           TShockData.CrossChat
                           TShockData.CrossTransfer
```

`HotReload.dll`：引用里**没有** MonoMod/ModFramework（先前的字符串命中只是普通文本）。

⇒ 目前**唯一会安装 MonoMod RuntimeDetour 的插件是 PGameAPI**，这是最可能的挂钩来源；
但 `PGameAPI 单独` 跑过一次是 WORLD_OK，故仍需**重复 N 次**统计失败率才能定论。

### 当前生产处置（临时）

直播服现为 `autocreate=3` + `difficulty=2`（大世界+大师），真机 rebuild 成功过一次；
在间歇性根因查清前，这**只是可用状态，不是已证实的修复**。

---

## ~~（已撤回）我曾认为已锁定并解决~~

**结论：触发者是 `PigeonRPG.ProgressLoot.dll`，且与「世界参数」强相关。**

独立临时沙箱二分（全程不影响在跑的四个服，`autocreate` 用 2/经典 做快速复现）：

| 测试集 | 参数 | 结果 |
|---|---|---|
| 仅 TShockAPI | 2 / 经典 | ✅ |
| A 半 10 插件（依赖闭合） | 2 / 经典 | ✅ |
| B1：PChrome.PVP + PigeonMiniGamesAPI + PeaceMode + PGameAPI | 2 / 经典 | ✅ |
| B2a：ProgressGuard + ProgressLoot + ProgressSync | 2 / 经典 | ❌ StackOverflow |
| PigeonRPG.ProgressGuard 单独 | 2 / 经典 | ✅ |
| PigeonRPG.ProgressSync 单独 | 2 / 经典 | ✅ |
| **PigeonRPG.ProgressLoot 单独** | 2 / 经典 | ❌ **StackOverflow** |
| **PigeonRPG.ProgressLoot 单独** | **3 / 大师 / random** | ✅ **11,849,183 B** |

⇒ 不是 OTAPI HookGen 本身坏，而是 **ProgressLoot 的 WorldGen 钩子在「中图 + 经典」参数组合下自递归**；
大图 + 大师参数下正常（这也解释了泰拉大陆 / 流光神域同样挂 ProgressLoot 却能建图）。

**已实施修复**：直播服改为 `autocreate=3` + `difficulty=2` + `worldevil=random`（`server.properties` + 清单 `自动建图=3`），
随后 TSM 真机 rebuild → `ready`，世界 6,886,177 → **11,964,749 B（8400×2400）**，日志 `Difficulty: 2`，**无 StackOverflow**。

**约束**：直播服必须保持 `autocreate=3 + difficulty=2`，否则会退回旧崩溃参数组合。

---

## 一、现象

带插件建图时退出码 `-1073741571`（STATUS_STACK_OVERFLOW），栈为：

```text
Terraria.WorldGen.nextCount
On.Terraria.WorldGen.mfwh_nextCount
```

仅保留 TShockAPI 建图正常。

## 二、静态检查结论

1. 在 PigeonRPG、PigeonPlugins、PigeonDimension 等源码中检索 `nextCount`，未发现业务插件直接订阅或调用该 Hook。
2. 二进制字符串 `mfwh_nextCount / hook_mfwh_nextCount / orig_mfwh_nextCount / hook_nextCount` 只出现在：

```text
Core\bin\OTAPI.dll
Core\bin\OTAPI.Runtime.dll
```

3. ILSpy 查看 `On.Terraria.WorldGen`，确认 OTAPI HookGen 同时生成了：

```text
orig_nextCount
hook_nextCount
orig_mfwh_nextCount
hook_mfwh_nextCount
nextCount event
mfwh_nextCount event
```

## 三、最终定位

- 触发插件：`PigeonRPG.ProgressLoot.dll` 的 WorldGen 钩子。
- 只在 **中图 + 经典** 参数组合下出现 `WorldGen.nextCount ↔ mfwh_nextCount` 自递归。
- `ProgressGuard` 单独、`ProgressSync` 单独、只留 TShockAPI 均正常；TSM 的独立沙箱二分不受在跑服影响。

## 四、解决方案与约束

- 直播服改为：`autocreate=3`（大世界 8400×2400）、`difficulty=2`（大师）、`worldevil=random`、`seed` 为空。
- TSM 真机 rebuild 已成功：世界 6,886,177 → 11,964,749 B，日志 `Width:8400 Height:2400 Difficulty:2`，无 StackOverflow。
- `/pout reset`、换图、删图重建对 `0.鸽子直播服` 已解禁。
- **硬约束**：必须保持 `autocreate=3 + difficulty=2`；改回中图/经典会复现崩溃。
