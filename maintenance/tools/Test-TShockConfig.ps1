<#
.SYNOPSIS
  PGame-TSManager · TShock 配置维护校验（只读）

.DESCRIPTION
  校验一个 TShock 服务端目录是否满足固定规则：
    1. tshock\config.json 使用中文键（汉化已应用），且没有残留英文键
    2. REST 令牌字段为中文「用户名 / 用户组」，不是 Username / UserGroupName
    3. tshock\sscconfig.json 保持英文（不翻译、不改键）
  只读校验，绝不修改任何文件。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Test-TShockConfig.ps1 -ServerPath 'D:\59934\Desktop\流光服\1.流光城'

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Test-TShockConfig.ps1 -ServerPath 'D:\59934\Desktop\流光服\2.RPG\1.泰拉大陆' -AsJson
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$ServerPath,
    [string]$MappingPath,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
if (-not $MappingPath) { $MappingPath = Join-Path $PSScriptRoot '..\config-translation\TransferPatch.json' }

$CJK = [regex]'[\u4e00-\u9fff]'
$results = New-Object 'System.Collections.Generic.List[object]'
function Add-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ check = $Name; ok = $Ok; detail = $Detail })
}

if (-not (Test-Path -LiteralPath $ServerPath)) { throw "服务端目录不存在: $ServerPath" }
$server = (Resolve-Path -LiteralPath $ServerPath).Path
$configPath = Join-Path $server 'tshock\config.json'
$sscPath = Join-Path $server 'tshock\sscconfig.json'
$restKeyCn = 'Rest外部应用令牌字典'
$restKeyEn = 'ApplicationRestTokens'

# ---------- 读取汉化映射（排除 SSC / TokenData 两组） ----------
$englishTShock = @{}
if (Test-Path -LiteralPath $MappingPath) {
    $map = Get-Content -Raw -Encoding UTF8 $MappingPath | ConvertFrom-Json
    foreach ($entry in $map.执行列表) {
        foreach ($p in $entry.翻译列表.PSObject.Properties) {
            if ($p.Name -like '*SscSettings*') { continue }
            if ($p.Name -like '*TokenData*') { continue }
            $englishTShock[$p.Name.Split('.')[-1]] = $p.Value
        }
    }
}
Add-Check '汉化映射文件' ($englishTShock.Count -gt 0) "共 $($englishTShock.Count) 条映射"

# ---------- config.json ----------
if (-not (Test-Path -LiteralPath $configPath)) {
    Add-Check 'config.json 存在' $false $configPath
}
else {
    $cfg = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json
    $settings = $cfg.Settings
    if ($null -eq $settings) {
        Add-Check 'config.json 结构' $false '缺少 Settings 节点'
    }
    else {
        $keys = @($settings.PSObject.Properties.Name)
        Add-Check 'config.json 结构' $true "Settings 共 $($keys.Count) 项"

        $englishPresent = @($keys | Where-Object { $englishTShock.ContainsKey($_) })
        if ($englishPresent.Count -eq 0) {
            Add-Check 'config.json 中文键' $true '未发现英文键，汉化已应用'
        }
        else {
            Add-Check 'config.json 中文键' $false ('发现英文键: ' + ($englishPresent -join ', '))
        }

        $missing = @($englishTShock.Values | Where-Object { $keys -notcontains $_ })
        if ($missing.Count -eq 0) {
            Add-Check 'config.json 覆盖度' $true "已覆盖全部 $($englishTShock.Count) 个映射字段"
        }
        else {
            Add-Check 'config.json 覆盖度' $true ("新版本可能新增字段，需更新映射: " + ($missing -join ', '))
        }

        $rest = $settings.$restKeyCn
        if ($null -eq $rest) { $rest = $settings.$restKeyEn }
        if ($null -eq $rest) {
            Add-Check 'REST 令牌字典' $true '未配置 REST 令牌（跳过）'
        }
        else {
            $bad = @()
            foreach ($t in $rest.PSObject.Properties) {
                $fields = @($t.Value.PSObject.Properties.Name)
                if (($fields -contains 'Username') -or ($fields -contains 'UserGroupName')) {
                    $bad += "$($t.Name): 仍是 Username/UserGroupName"
                }
                elseif (-not (($fields -contains '用户名') -and ($fields -contains '用户组'))) {
                    $bad += "$($t.Name): 缺少 用户名/用户组"
                }
            }
            if ($bad.Count -eq 0) {
                Add-Check 'REST 令牌字段' $true "OK（$(@($rest.PSObject.Properties).Count) 个令牌）"
            }
            else {
                Add-Check 'REST 令牌字段' $false ($bad -join '; ')
            }
        }
    }
}

# ---------- sscconfig.json ----------
if (-not (Test-Path -LiteralPath $sscPath)) {
    Add-Check 'SSC 保持英文' $true '未启用 SSC（文件不存在，跳过）'
}
else {
    $ssc = Get-Content -Raw -Encoding UTF8 $sscPath | ConvertFrom-Json
    $sk = @($ssc.Settings.PSObject.Properties.Name)
    $cn = @($sk | Where-Object { $CJK.IsMatch($_) })
    if ($cn.Count -eq 0) {
        Add-Check 'SSC 保持英文' $true "OK（$($sk.Count) 项，全英文）"
    }
    else {
        Add-Check 'SSC 保持英文' $false ('发现中文键: ' + ($cn -join ', '))
    }
}

# ---------- 输出 ----------
$failed = @($results | Where-Object { -not $_.ok })
$ok = ($failed.Count -eq 0)

if ($AsJson) {
    [pscustomobject]@{
        server  = $server
        result  = if ($ok) { 'OK' } else { 'FAIL' }
        checks  = $results
    } | ConvertTo-Json -Depth 6
}
else {
    Write-Host ""
    Write-Host "PGame-TSManager · TShock 配置校验" -ForegroundColor Cyan
    Write-Host "服务端: $server"
    Write-Host ("-" * 60)
    foreach ($r in $results) {
        $tag = if ($r.ok) { '[OK]  ' } else { '[FAIL]' }
        $color = if ($r.ok) { 'Green' } else { 'Red' }
        Write-Host ("{0} {1}" -f $tag, $r.check) -ForegroundColor $color -NoNewline
        Write-Host ("  - {0}" -f $r.detail)
    }
    Write-Host ("-" * 60)
    if ($ok) { Write-Host 'RESULT: OK' -ForegroundColor Green }
    else { Write-Host 'RESULT: FAIL' -ForegroundColor Red }
}

if (-not $ok) { exit 1 }
exit 0