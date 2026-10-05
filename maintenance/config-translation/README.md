# 配置文件中文映射

用途：维护 TShock `config.json` 的中文键映射，使配置文件键名中文化。

## 文件

- `TransferPatch.json`：权威映射表。键 = 原始英文成员全名，值 = 中文键名。
  - `执行列表[].目标程序集`：`plugins/TShockAPI.dll`
  - `执行列表[].目标类`：
    - `TShockAPI.Configuration.TShockSettings`（全局配置，共 146 项）
    - `Rests.SecureRest/TokenData`（REST 令牌字段，2 项）

## 固定规则

- **只映射 config.json 的键**，不改值、不改结构。
- **SSC 不翻译**：`sscconfig.json` 保持英文，映射表里不包含 `SscSettings`。
- **REST 令牌字段**固定为中文「用户名 / 用户组」，见 ../rest-fixes。
- **不修改 /help**，见 ../help-lock。

## 应用方式（两种）

1. 离线 IL 补丁（推荐）：官方更新替换 `TShockAPI.dll` 后，用 `ApplyTransferPatch`
   按 `TransferPatch.json` 改写 JSON 属性名。
2. 运行时插件：部分服务器用 `tshock\TransferPatch.json`（同一份映射格式，`启用` 开关控制）。

## 维护流程

1. 官方换版后先生成一份英文 config.json，对比映射表找出**新增/删除字段**。
2. 更新 `TransferPatch.json`（保持目标类只有 TShockSettings + TokenData）。
3. 应用映射并运行校验：

```powershell
powershell -ExecutionPolicy Bypass -File ..\tools\Test-TShockConfig.ps1 -ServerPath 'D:\59934\Desktop\流光服\1.流光城'
```

校验会报出残留英文键（说明汉化没应用）与新增字段（说明映射需更新）。