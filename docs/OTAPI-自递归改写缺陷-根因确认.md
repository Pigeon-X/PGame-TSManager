# OTAPI 自递归改写缺陷 —— `WorldGen.nextCount` 爆栈根因（已确认）

> 日期：2026-10-09
> 来源：服务器/RPG 侧 IL 分析（Cecil）+ TSM 独立复核与全局扫描
> 结论：**与任何业务插件无关**，是 OTAPI / ModFramework 改写层的缺陷。

## 一、机制（`OTAPI.dll` IL）

```text
WorldGen.nextCount        (入口) : 派发 Invoke nextCount 事件 → call mfwh_nextCount（原方法，非尾调用）
WorldGen.mfwh_nextCount   (原体) : 4 处自递归 -> call nextCount（入口）        ← 应指向 mfwh_nextCount
WorldGen.mfwh_countTiles  (原体) : 18 处 -> call nextCount（入口）            ← 应指向 mfwh_*
```

⇒ 每层递归 = **入口帧 + 原方法帧**（原版仅 1 帧），且**每层重跑一次 hook 事件派发**
⇒ 栈占用约 **2×**，爆栈阈值降到约一半。

`nextCount` 是 4 路 flood fill，深度随**连通地形路径长度**增长 ⇒ **间歇性、与世界形状/时序相关**。

## 二、全局影响范围（TSM 扫描 `OTAPI.dll`，2026-10-09）

「`mfwh_*` 方法体内回调**自己入口**」的共 **100 处**，例如：

```text
Terraria.WorldGen   nextCount / countTiles / GenerateWorld / KillTile / SpawnTownNPC / Cavinator …
Terraria.Main       Update / Initialize / DrawMap / NewText / MouseText …
Terraria.Item       Prefix / SetDefaults / NewItem / DefaultToPlaceableTile …
Terraria.NPC        SpawnNPC / FindClosestPlayer / HealEffect …
Terraria.Projectile Kill / NewProjectile / CutTiles / Shimmer …
Terraria.WorldFile  ValidateWorld / _SaveWorld
Terraria.NetMessage SendTileSquare / ResyncTiles
Terraria.Netplay / Wiring.Teleport / RemoteClient …
```

⇒ 不止 `nextCount`：这 100 个方法全部是「**2× 栈 + 重复事件派发**」。

## 三、这解释了此前的全部观察

| 观察 | 是否被解释 |
|---|---|
| 20 个插件 DLL 的 IL + 字符串里 `nextCount/countTiles/mfwh_` 一处都没有 | ✅ 因为问题不在插件 |
| 零热重载也会崩（沙箱直跑） | ✅ 与热重载无关 |
| 同一 seed 292804472 复跑却 OK | ✅ 真实世界形状/深度不同（`-seed` 相同但生成路径/时序不同） |
| 中图 + 经典更易崩 | ✅ 地形连通路径更长 / 栈更紧张时更容易越界 |

**PigeonRPG / PGameAPI / HotReload 全部洗清。**

## 四、修法（优先级）

1. **包装层 `[ThreadStatic]` 递归重入保护**（重入直接走原方法、不再派发事件）→ 栈立即减半，改动最小；
2. **治本**：把这批 `mfwh_*` 体内的自递归/内部调用由「入口」改指 `mfwh_*` / trampoline（纯 IL 操作数改写）；
3. 备选：`nextCount` 改迭代实现。

> TSM 侧：项目**已有 OTAPI 补丁管线**（`Core\bin\OTAPI.dll.orig-startup` 即补丁前原件）。
> 可将 ② 做成同款 IL 补丁 —— **待窗口 + 授权后实施并做世界生成回归**。

## 五、当前生产处置

- 直播服（id 0）当前运行正常（大世界 + 大师，世界 11,964,749 B）；
  「大图+大师」是**可用状态**，不是已证实的修复（根因是上述 OTAPI 缺陷）。
- 该缺陷**全服共用**（同一份 `OTAPI.dll`），因此可能在任何服的**建图/重建**时随机出现。
