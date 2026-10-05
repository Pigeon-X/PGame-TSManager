# 维护工具

## Test-TShockConfig.ps1

只读校验一个 TShock 服务端目录是否符合固定规则：

1. `tshock\config.json` 使用中文键（汉化已应用），无残留英文键。
2. REST 令牌字段为中文「用户名 / 用户组」（不是 `Username` / `UserGroupName`）。
3. `tshock\sscconfig.json` 保持英文（未翻译）。

```powershell
# 人读
powershell -ExecutionPolicy Bypass -File .\Test-TShockConfig.ps1 -ServerPath 'D:\59934\Desktop\流光服\1.流光城'

# 机器读（JSON）
powershell -ExecutionPolicy Bypass -File .\Test-TShockConfig.ps1 -ServerPath '<服务端目录>' -AsJson
```

- 退出码：0 = 全部通过，1 = 有 FAIL。
- 依赖映射表 `..\config-translation\TransferPatch.json`（可用 -MappingPath 覆盖）。
- 只读，不修改任何文件。