<#
  按旧版 TSManager 逻辑整理目录：
    1.PigeonServers\<服>\   只留  config.json + tshock\
    世界 → 根 Worlds\      （改名成 <服>.wld，避免重名）
    插件 → 根 Plugins\ 总库
    其余（exe/bin/i18n/runtimes/x64/GeoIP.dat/ServerPlugins/server.properties/Logs/...）全部删除
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManagerDir)) { throw "找不到 $ManagerDir" }
$ManagerDir = (Resolve-Path -LiteralPath $ManagerDir).Path
$serversRoot = Join-Path $ManagerDir '1.PigeonServers'
$worldsDir   = Join-Path $ManagerDir 'Worlds'
$poolDir     = Join-Path $ManagerDir 'Plugins'
New-Item -ItemType Directory -Force -Path $worldsDir, $poolDir | Out-Null

$keep = @('config.json','tshock')
$removedCount = 0

foreach ($s in (Get-ChildItem -LiteralPath $serversRoot -Directory)) {
    Write-Host ("=== " + $s.Name + " ===") -ForegroundColor Cyan
    $cfgFile = Join-Path $s.FullName 'config.json'
    $propsFile = Join-Path $s.FullName 'server.properties'
    $old = $null
    if (Test-Path -LiteralPath $cfgFile) { try { $old = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json } catch {} }

    # --- 端口 / 世界 / 插件 ---
    $port = 0; $restPort = 0; $worldSrc = $null
    if ($old -and $old.'备注' -match '端口\s*(\d+)') { $port = [int]$Matches[1] }
    if ($old -and $old.'备注' -match 'REST\s*(\d+)') { $restPort = [int]$Matches[1] }
    if ($propsFile -and (Test-Path -LiteralPath $propsFile)) {
        $pl = Get-Content -LiteralPath $propsFile -Encoding UTF8 | Where-Object { $_ -match '^(world|port)=' }
        foreach ($line in $pl) {
            if ($line -match '^port=(\d+)') { $port = [int]$Matches[1] }
            if ($line -match '^world=(.+)$') {
                $w = $Matches[1].Trim()
                $cand = if ([IO.Path]::IsPathRooted($w)) { $w } else { Join-Path $s.FullName ($w -replace '^\./','') }
                if (Test-Path -LiteralPath $cand) { $worldSrc = $cand }
            }
        }
    }
    if (-not $worldSrc) {
        foreach ($d in @('world','地图')) {
            $p = Join-Path $s.FullName $d
            if (Test-Path -LiteralPath $p) {
                $w = Get-ChildItem -LiteralPath $p -File -Filter *.wld | Sort-Object Length -Descending | Select-Object -First 1
                if ($w) { $worldSrc = $w.FullName; break }
            }
        }
    }
    if ($restPort -eq 0) {
        $tc = Join-Path $s.FullName 'tshock\config.json'
        if (Test-Path -LiteralPath $tc) { try { $j = Get-Content -Raw -Encoding UTF8 $tc | ConvertFrom-Json; $restPort = [int]$j.Settings.'Rest的端口' } catch {} }
    }

    $plugins = @()
    $sp = Join-Path $s.FullName 'ServerPlugins'
    if (Test-Path -LiteralPath $sp) {
        $plugins = Get-ChildItem -LiteralPath $sp -File -Filter *.dll | Where-Object { $_.Name -notmatch '\.bak' } | Select-Object -ExpandProperty Name | Sort-Object
        foreach ($f in (Get-ChildItem -LiteralPath $sp -File | Where-Object { $_.Name -notmatch '\.bak' })) {
            $dest = Join-Path $poolDir $f.Name
            if (-not (Test-Path -LiteralPath $dest)) { Copy-Item -LiteralPath $f.FullName -Destination $dest -Force }
        }
    }
    if ($plugins -notcontains 'TShockAPI.dll') { $plugins = @('TShockAPI.dll') + $plugins }

    # --- 世界搬去 Worlds\，改名为 <服>.wld ---
    $worldName = $s.Name + '.wld'
    if ($worldSrc) {
        $worldDest = Join-Path $worldsDir $worldName
        if ($DryRun) { Write-Host ("  [试运行] 世界: " + $worldSrc + " -> " + $worldDest) }
        else { Copy-Item -LiteralPath $worldSrc -Destination $worldDest -Force }
        $worldName = $s.Name + '.wld'
        Write-Host ("  世界: " + $worldName) -ForegroundColor DarkGray
    } else { Write-Host "  世界: 未找到（config.json 里需手工填「世界」）" -ForegroundColor Yellow }

    # --- 写新的 config.json（旧 TSM 字段） ---
    $manifest = [ordered]@{
        '服务器名称'   = $s.Name
        '启用'         = $true
        '世界'         = $worldName
        '语言'         = 7
        '端口'         = $port
        'REST端口'     = $restPort
        '最大玩家'     = 252
        'IP'           = '0.0.0.0'
        '密码'         = ''
        '启动参数'     = ''
        '插件'         = @($plugins)
        '插件总库'     = 'Plugins'
        '覆盖插件目录' = $true
        '备注'         = ("{0} 端口 {1} / REST {2}" -f $s.Name, $port, $restPort)
    }
    if (-not $DryRun) { [System.IO.File]::WriteAllText($cfgFile, ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false))) }
    Write-Host ("  端口: {0} / REST {1} / 插件 {2} 个" -f $port, $restPort, $plugins.Count) -ForegroundColor DarkGray

    # --- 删除其余 ---
    foreach ($item in (Get-ChildItem -LiteralPath $s.FullName -Force)) {
        if ($keep -contains $item.Name) { continue }
        if ($DryRun) { Write-Host ("  [试运行] 删除 " + $item.Name) }
        else { Remove-Item -LiteralPath $item.FullName -Recurse -Force; $removedCount++ }
    }
}

Write-Host ""
Write-Host ("完成。1.PigeonServers 下各服现在只有 config.json + tshock\；世界在 Worlds\，插件在 Plugins\。" -f $removedCount) -ForegroundColor Green