# REST 修复

REST 外部应用令牌字典的**字段名必须中文化**，否则令牌读不出来（用户名/用户组为空）。

## 正确写法

```json
{
  "Rest外部应用令牌字典": {
    "xztxzt0928": {
      "用户名": "流光之城",
      "用户组": "superadmin"
    }
  }
}
```

对应的英文原字段是 `ApplicationRestTokens` → `Username` / `UserGroupName`。

## 常见错误

```json
{ "xztxzt0928": { "Username": null, "UserGroupName": null } }   // 错误：英文键 + 空值
```

- 字段名必须是「用户名 / 用户组」，不能是 `Username` / `UserGroupName`。
- 值必须是真实用户名 / 用户组，不能是 `null`。

## 规则

- REST 令牌字段使用中文「用户名 / 用户组」（映射表 `Rests.SecureRest/TokenData` 两项）。
- SSC 不翻译、不改键。
- 不修改 /help。
- 每次修复记录提交号和上报回执。

## 校验

```powershell
powershell -ExecutionPolicy Bypass -File ..\tools\Test-TShockConfig.ps1 -ServerPath '<服务端目录>'
```

`REST 令牌字段` 一项会直接指出仍是英文键或值缺失的令牌。