# `/pout reset` 归属约定（与 RPG 完全一致）

日期：2026-10-09

> 一句话：**入口 + 清表归 FixTools；换图归 TSM。**

## 逐项归属

| # | 环节 | 归属 |
|---|---|---|
| ① | 命令本体、权限 `pout.use`、确认、倒计时、踢人 | **FixTools / 服务器侧** |
| ② | 导出存档、`BeforeCMD`、`ClearSql`（34 条，**不含 `users`**）、进度锁重置 | **FixTools / 服务器侧** |
| ③ | `TsmRebuildClient` → `POST /tsm/world/rebuild`、状态码处理、拒绝时**零降级** | **FixTools / 服务器侧** |
| ④ | 停服、备份、删旧图、按 `server.properties` 出图、起服、发事件 | **TSM** |
| ⑤ | 世界文件 / `server.properties` 写入 | **TSM**（FixTools **只读**） |
| ⑥ | `users` / PGameAPI 表 | **PGameAPI**（任何一方都不得清） |

## 顺序（不可颠倒）

```text
/pout reset yes
   → FixTools：确认 + 倒计时 + 踢人 + 导出存档 + BeforeCMD
   → FixTools：清 34 张 RPG 表（不含 users）
   → FixTools：TsmRebuildClient 请求 POST /tsm/world/rebuild
        · 202 accepted → 继续，等 world.rebuild.ready
        · 401/400/404/409/503 → 拒绝执行、**零降级**（不做"只清表"或"只换图"）
   → TSM：停服 → 备份(.bak-<时间戳>) → 删旧图 → 按 server.properties 出图 → 起服 → 发事件
```

## 硬约束

- **禁止**直接调 `POST /tsm/world/rebuild` 当"重置"（只换图不清表 → 语义分叉）。
- 同一服已有 rebuild 在跑 → `409 rebuild_in_progress`（不排队、不覆盖）。
- `users` / `grouplist` / PGameAPI 表：**ID 1-5 必须保留**。

## 各服可用性

| 服 | serverId | `/pout reset` 可用 |
|---|---|---|
| 鸽子直播服 | `0` | ❌ **暂不可用** —— 该服带全插件建图会 `WorldGen.nextCount` 自递归 StackOverflow；插件修好前禁止换图/删图重建 |
| 流光城 | `1` | ⚠ 需确认其 profile 的世界/建图参数后再启用 |
| 泰拉大陆 | `2` | ✅ 已实测（2026-10-08 组合演练通过） |
| 流光神域 | `3` | ✅ 已实测（同种子重建结果一致，属预期） |
