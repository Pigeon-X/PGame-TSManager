# 鸽子直播服：WorldGen.nextCount 自递归诊断

> 时间：2026-10-09
> 范围：`serverId=0` 鸽子直播服建图 StackOverflow
> 状态：已定位到 HookGen 包装层；尚未锁定具体触发插件

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

## 三、当前判断

- 直接递归点是 OTAPI.Runtime 的 HookGen 包装层，不是插件源码里显式的 `WorldGen.nextCount` 订阅。
- 某个已加载插件可能通过另一条 MonoMod/OTAPI Hook 链改变了 `mfwh_nextCount -> orig` 的指向，导致 `orig` 最终又回到 `nextCount`。
- 现阶段不能把问题归因到单个插件；需要在隔离环境按插件子集二分加载，复现建图后记录最后加入的插件。

## 四、安全约束

- 直播服已用干净环境预生成世界，当前正常 running。
- 在复现插件确定并修复前，禁止对 `0.鸽子直播服` 执行换图、删图重建或 `/pout reset`。
- 二分测试只能在临时目录、临时端口和复制出的世界/插件集合中进行，不得复用直播服运行沙箱。
