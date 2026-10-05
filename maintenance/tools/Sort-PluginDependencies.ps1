<#
  把 Plugins\ 里的「插件依赖库」挪到 bin\。
  判定规则：DLL 元数据里不含 TShockAPI / TerrariaPlugin 的，就是插件的依赖库（如 linq2db、Microsoft.Data.Sqlite、SQLitePCLRaw.*），
  它们不是 TShock 插件，不应该放在插件总库里被当成插件加载。
  同时从每服 config.json 的「插件」清单里移除这些依赖。
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManagerDir)) { throw "找不到 $ManagerDir" }
$ManagerDir = (Resolve-Path -LiteralPath $ManagerDir).Path
$pool = Join-Path $ManagerDir 'Plugins'
$bin  = Join-Path $ManagerDir 'bin'
if (-not (Test-Path -LiteralPath $pool)) { throw "找不到插件总库 $pool" }
if (-not (Test-Path -LiteralPath $bin))  { New-Item -ItemType Directory -Force -Path $bin | Out-Null }

$moved = @()
foreach ($f in (Get-ChildItem -LiteralPath $pool -File -Filter *.dll | Sort-Object Name)) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $isPlugin = $ascii.Contains('TShockAPI') -or $ascii.Contains('TerrariaPlugin')
    if ($isPlugin) { Write-Host ("  插件   " + $f.Name) -ForegroundColor DarkGray; continue }

    $dest = Join-Path $bin $f.Name
    Write-Host ("  依赖   " + $f.Name + "  ->  bin\") -ForegroundColor Yellow
    $moved += $f.Name
    if ($DryRun) { continue }
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Force }
    Move-Item -LiteralPath $f.FullName -Destination $dest -Force
}

if ($moved.Count -gt 0) {
    Write-Host ""
    Write-Host "[更新各服 config.json 的插件清单]" -ForegroundColor Cyan
    foreach ($s in (Get-ChildItem -LiteralPath (Join-Path $ManagerDir '1.PigeonServers') -Directory)) {
        $cfg = Join-Path $s.FullName 'config.json'
        if (-not (Test-Path -LiteralPath $cfg)) { continue }
        $m = Get-Content -Raw -Encoding UTF8 $cfg | ConvertFrom-Json
        $before = @($m.'插件').Count
        $after = @($m.'插件' | Where-Object { $moved -notcontains $_ })
        $m.'插件' = @($after)
        if (-not $DryRun) { [System.IO.File]::WriteAllText($cfg, ($m | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false))) }
        Write-Host ("  {0}: {1} -> {2} 个" -f $s.Name, $before, $after.Count) -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host ("完成：移动依赖 {0} 个到 bin\。" -f $moved.Count) -ForegroundColor Green