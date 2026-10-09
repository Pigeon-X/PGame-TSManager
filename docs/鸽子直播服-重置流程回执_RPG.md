# 鸽子直播服：重置方法回执（给 RPG 会话）

> 适用服务器：`0.鸽子直播服` / `serverId=0`
> 规则：与泰拉大陆、流光神域同款
> 时间：2026-10-09

## 一、两个入口

| 目标 | 命令 | 是否换图 | 是否清 `users` |
| --- | --- | --- | --- |
| 只重置玩家/RPG/经济/商店数据 | `/pout sql yes` | 否 | 否 |
| 完整重置：清数据 + 换图 | `/pout reset yes` | 是，TSM 执行 | 否 |

## 二、只重置玩家数据

执行：

```text
/pout sql yes
```

行为：

- 直接执行 `重置清理数据表`，当前 34 条。
- 包含 SSC 角色、RPG 档案/任务/技能/进度、经济余额/审计、商店库存/购买/审计、玩家每日数据等。
- **不删除** `users`、`grouplist`、PGameAPI 表。
- **不换图**，不请求 TSM rebuild。
- 不执行 `重置前执行指令`。

## 三、完整重置

执行：

```text
/pout reset yes
```

顺序：

1. FixTools 广播并倒计时 6 秒。
2. 踢出全部在线玩家，确保 SSC/角色保存。
3. `WritePlayer.ExportAll` 导出玩家存档。
4. 执行 `重置前执行指令`，当前 38 条。
5. 执行 `ClearSql`，当前 34 条，不含 `users`。
6. 清空并写回 FixTools 进度锁。
7. FixTools `TsmRebuildClient` 调：

```http
POST /tsm/world/rebuild
serverId=0
confirm=true
backup=true
startAfterRebuild=true
```

8. TSM 返回 `202 accepted` 后：
   - 停服；
   - 备份旧世界；
   - 删除旧图；
   - 按 `server.properties` 出图；
   - 起服；
   - 发 `world.rebuild.ready`。
9. TSM 返回 400/401/404/409/503/500 时：
   - FixTools 拒绝降级；
   - 数据已清，但地图不换；
   - 修复 TSM 问题后重试。

## 四、归属边界

| 资源 | 唯一负责方 |
| --- | --- |
| `/pout reset` 命令、权限、确认、倒计时、踢人 | FixTools |
| 导出存档、BeforeCMD、ClearSql、进度锁重置 | FixTools |
| `TsmRebuildClient` 请求与状态码处理 | FixTools |
| 停服/备份/删旧图/出图/起服/发事件 | TSM |
| `Worlds\*.wld` / `server.properties` 写入 | TSM |
| `users` / `grouplist` / PGameAPI 表 | PGameAPI |

一句话：**入口 + 清表归 FixTools；换图归 TSM。**

## 五、0 号直播服当前配置

- `TSM对接.json`：启用，地址 `http://127.0.0.1:8765`，`TSM服务器ID=0`。
- `自动修复地图缺失=false`。
- `重置后执行指令=[]`。
- `重置时删除文件` 无 `world/*.wld` / `.bak`。
- `重置清理数据表` 无 `DELETE FROM users`。

不要直接调 `POST /tsm/world/rebuild` 当重置：它只会换图，不会执行 FixTools 的清表和进度锁重置。

## 六、建图崩溃说明

- 此前对 `PigeonRPG.ProgressLoot` 的归因已撤回；PigeonRPG 无需改动。
- `WorldGen.nextCount` StackOverflow 是间歇性问题，根因仍在查；当前重点怀疑唯一安装 MonoMod RuntimeDetour 的 PGameAPI。
- 直播服 `/pout reset`、换图、删图重建均已解禁。
- 若再次出现 StackOverflow，保留 stdout + stderr 原始日志并通知 TSM。

## 七、`/pout sql yes` 源码级实现细节

入口注册：

```csharp
public static string pt => "pout";
public static string Prem => "pout.use";

Commands.ChatCommands.Add(
    new Command(Prem, PoutCmd.Pouts, "pout", "pt"));
```

分派：

```csharp
case "改数据":
case "sql":
    ClearSqlInfo(args, plr);
    break;
```

确认条件：

```csharp
if (args.Parameters.Count < 2 ||
    !args.Parameters[1].Equals("yes", StringComparison.OrdinalIgnoreCase))
{
    // 输出不可逆警告，并提示 /pout sql yes
    return;
}
ClearSql(plr);
```

执行语义：

```csharp
var ok = 0;
foreach (var sql in Config.ClearSql)
{
    try
    {
        TShock.DB.Query(sql);
        ok++;
    }
    catch (Exception ex)
    {
        TShock.Log.ConsoleWarn(
            $"[{PluginName}]：重置SQL({sql})执行失败: {ex.Message}");
    }
}
```

关键差异与注意事项：

- `Config.ClearSql` 类型是 `HashSet<string>`，执行顺序不保证；同名 SQL 会去重。
- 逐条执行；单条失败只记日志，继续执行后续 SQL；**没有事务、没有整体回滚**。
- 直接使用当前 TShock DB 连接执行；直播服目标是 `1.pgs`。
- 不踢人、不倒计时、不导出存档、不执行 BeforeCMD。
- 不调用 TSM，不换图，不写 World / `server.properties`。
- 不清 FixTools `UnLockNpc` 进度锁。
- 不清 `users` / `grouplist` / PGameAPI 表。
- `/poutreset`（`流光重置`）是完整重置别名，不等价于 `/pout sql yes`。

当前直播服实际 `重置清理数据表` 共 34 条：

```text
DELETE FROM tsCharacter
DELETE FROM hourly_online_snapshot
DELETE FROM itembans
DELETE FROM personalpermissions
DELETE FROM pigeonrpg_economy
DELETE FROM pigeonrpg_economy_audit
DELETE FROM pigeonrpg_reward_log
DELETE FROM pigeonrpg_shop_audit_realm
DELETE FROM pigeonrpg_shop_audit_terra
DELETE FROM pigeonrpg_shop_purchase_realm
DELETE FROM pigeonrpg_shop_purchase_terra
DELETE FROM pigeonrpg_shop_stock_realm
DELETE FROM pigeonrpg_shop_stock_terra
DELETE FROM player_daily_stat
DELETE FROM playerbans
DELETE FROM projectilebans
DELETE FROM regions
DELETE FROM rememberedpos
DELETE FROM research
DELETE FROM rpg_account_progress
DELETE FROM rpg_audit_log
DELETE FROM rpg_player_active_side_tasks
DELETE FROM rpg_player_active_task
DELETE FROM rpg_player_profile
DELETE FROM rpg_player_task_counter
DELETE FROM rpg_player_task_progress
DELETE FROM rpg_progress_hardmode
DELETE FROM rpg_progress_migration_state
DELETE FROM rpg_progress_outbox
DELETE FROM rpg_progress_prehardmode
DELETE FROM rpg_skill_loadout
DELETE FROM task_execution_logs
DELETE FROM tilebans
DELETE FROM warps
```

## 八、FixTools 新 DLL 热加载回执

- 直播服沙箱：
  `Core\_runtime\鸽子直播服\ServerPlugins\FixTools.dll`
- SHA256：
  `5ACE89538E6EE8ED8250E939E28896E72D74CB966227794CC9C0459E75C2CB5D`
- TSM 热加载：

```http
POST /tsm/servers/0/plugins/reload {"plugin":"FixTools"}
→ 200
{"ok":true,"plugin":"FixTools","found":true,"output":"流光系统 v2026.9.28 已加载","error":null}
```

- 直播服仍为 `running`，端口 `2020/7877`，`rebuilding=false`。
- 未重启 TShock、未换图、未执行 `/pout reset` 或 `/pout sql yes`。
- 至此 id 0/1/2/3 四服都已使用同一版 FixTools 文案修复 DLL。
