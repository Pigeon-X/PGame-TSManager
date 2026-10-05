# SSC 冻结规则

SSC（服务端角色）配置保持英文原文。

- 不翻译键名，不修改字段含义。
- 汉化映射表**不包含** `TShockAPI.Configuration.SscSettings`。
- 典型英文键：`Enabled` / `ServerSideCharacterSave` / `LogonDiscardThreshold`
  / `StartingHealth` / `StartingMana` / `StartingInventory`
  / `WarnPlayersAboutBypassPermission` / `KeepPlayerAppearance`。
- TShock 更新时只做兼容性检查，不改键。

## 校验

```powershell
powershell -ExecutionPolicy Bypass -File ..\tools\Test-TShockConfig.ps1 -ServerPath '<服务端目录>'
```

`SSC 保持英文` 一项出现中文键即 FAIL。