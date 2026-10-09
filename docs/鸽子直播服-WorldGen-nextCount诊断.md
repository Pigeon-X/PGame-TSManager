# 鸽子直播服：WorldGen.nextCount 自递归诊断

> 时间：2026-10-09
> 范围：`serverId=0` 鸽子直播服建图 StackOverflow
> 状态：**根因已确认（IL 级）** —— OTAPI/ModFramework 改写缺陷：`mfwh_*` 体内自递归回调"入口"，
> 导致栈占用 2× + 重复派发 hook 事件。**与任何插件无关。**（先前的 ProgressLoot / PGameAPI / 种子假设均已排除）

---

## ✅ 已确认根因（2026-10-09，服务器/RPG 侧 IL 分析 + TSM 独立复核）

**机制**（`OTAPI.dll`，Cecil 读取）：

```text
WorldGen.nextCount        (入口) : 派发 Invoke nextCount 事件 → call mfwh_nextCount
WorldGen.mfwh_nextCount   (原体) : 4 处自递归全部 call nextCount （应 call mfwh_nextCount）
WorldGen.mfwh_countTiles  (原体) : 18 处 call nextCount（应走 mfwh_*）
```

⇒ 每层递归 = **入口帧 + 原方法帧**（原版 1 帧），且**每层重跑一次 hook 事件派发** ⇒
栈占用约 **2×**，爆栈阈值降到约一半。`nextCount` 是 4 路 flood fill，深度随**连通地形路径长度**增长
⇒ **间歇性、与世界形状/时序相关、与插件和热重载无关**。

**这解释了此前全部观察**：20 个插件 DLL 无 `nextCount/countTiles/mfwh_`、零热重载也崩、同 seed 有时 OK。

### 影响范围（TSM 全局扫描 `OTAPI.dll`，2026-10-09）

「`mfwh_*` 方法体内回调**自己入口**」的共 **100 处**（示例）：

```text
Terraria.WorldGen   nextCount / countTiles(调 nextCount) / GenerateWorld / KillTile / SpawnTownNPC …
Terraria.Main       Update / Initialize / DrawMap / NewText …
Terraria.Item       Prefix / SetDefaults / NewItem …
Terraria.NPC        SpawnNPC / FindClosestPlayer …
Terraria.Projectile Kill / NewProjectile / CutTiles …
Terraria.WorldFile  ValidateWorld / _SaveWorld …
```

⇒ 不止 `nextCount`：**这 100 个方法全部是「2× 栈 + 重复事件」**。
PigeonRPG / PGameAPI / HotReload 全部洗清。

### 修法（优先级）

1. **[ThreadStatic] 递归重入保护**：包装层重入时直接走原方法、不再派发事件 → 栈立即减半，改动最小；
2. **治本**：把这批 `mfwh_*` 体内的自递归/内部调用由「入口」改指 `mfwh_*` / trampoline（纯 IL 操作数改写）；
3. 备选：`nextCount` 改迭代实现。

> TSM 侧现状：项目**已有 OTAPI 补丁管线**（`Core\bin\OTAPI.dll.orig-startup` 即补丁前原件），
> 可将上述 ② 做成与现有补丁同款的 IL 补丁；待窗口+授权后实施并回归。

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


## 三、当前结论

- `PigeonRPG.ProgressLoot` 的首次 CRASH 不可复现，20 插件 DLL 中无 `nextCount/countTiles/mfwh_`；PigeonRPG 无需改动。
- 崩溃是间歇性问题；大图+大师不是已证实修复，只是当前未触发。
- 唯一会安装 MonoMod RuntimeDetour 的插件是 PGameAPI，作为最可能来源继续 N 次重复统计。
- 直播服已解禁 `/pout reset`、换图、删图重建；若再出现 StackOverflow，保留 stdout+stderr 并通知 TSM。
