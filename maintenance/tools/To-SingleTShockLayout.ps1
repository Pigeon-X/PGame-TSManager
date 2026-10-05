<#
  把 PGame-TSManager 变成一个 TShock：
    - 运行时（TShock.Server.exe / bin / i18n / runtimes / x64 / GeoIP.dat）只保留在管理器根目录
    - 插件目录（ServerPlugins）只保留在管理器根目录，三服共用
    - 每个服只留：config.json(TSM) + server.properties + tshock\ + world\ + Logs\
  实测结论：TShock 的插件目录固定 = exe 所在目录，因此“一个 exe”必然“一套插件”。
  被移出的文件不会删除，统一进 _归档_<时间戳>\。
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$serversDir = Join-Path $ManagerDir '1.PigeonServers'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archive = Join-Path $ManagerDir ("_归档_" + $stamp)

if (-not (Test-Path -LiteralPath $serversDir)) { throw "找不到 $serversDir" }
$servers = @(Get-ChildItem -LiteralPath $serversDir -Directory)
if ($servers.Count -eq 0) { throw "1.PigeonServers 下没有服务器目录" }

function Move-ToArchive([string]$path, [string]$sub) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $destDir = Join-Path $archive $sub
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    $dest = Join-Path $destDir (Split-Path -Leaf $path)
    if ($DryRun) { Write-Host ("      [试运行] 归档 " + $path) -ForegroundColor DarkGray; return }
    Move-Item -LiteralPath $path -Destination $dest -Force
}

$runtimeFiles = @('TShock.Server.exe', 'GeoIP.dat')
$runtimeDirs = @('bin', 'i18n', 'runtimes', 'x64')

Write-Host "[1/5] 建立根共享运行时" -ForegroundColor Cyan
$src = ($servers | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'TShock.Server.exe') } | Select-Object -First 1)
if (-not $src) { throw '任何服务器目录里都找不到 TShock.Server.exe' }
$srcPath = $src.FullName
Write-Host ("      运行时来源: " + $src.Name)
foreach ($f in $runtimeFiles) {
    $from = Join-Path $srcPath $f
    if (Test-Path -LiteralPath $from) { if (-not $DryRun) { Copy-Item -LiteralPath $from -Destination (Join-Path $ManagerDir $f) -Force }; Write-Host ("      " + $f) -ForegroundColor DarkGray }
}
foreach ($d in $runtimeDirs) {
    $from = Join-Path $srcPath $d
    if (-not (Test-Path -LiteralPath $from)) { $from = $null; foreach ($s in $servers) { $c = Join-Path $s.FullName $d; if (Test-Path -LiteralPath $c) { $from = $c; break } } }
    if ($from) { if (-not $DryRun) { Copy-Item -LiteralPath $from -Destination (Join-Path $ManagerDir $d) -Recurse -Force }; Write-Host ("      " + $d + "\") -ForegroundColor DarkGray }
}

Write-Host "[2/5] 建立根共享 ServerPlugins（三服共用）" -ForegroundColor Cyan
$lib = Join-Path $ManagerDir 'Plugins'
$rootSp = Join-Path $ManagerDir 'ServerPlugins'
if (-not $DryRun) { New-Item -ItemType Directory -Force -Path $rootSp | Out-Null }
if (Test-Path -LiteralPath $lib) {
    $libDll = Get-ChildItem -LiteralPath $lib -File -Filter *.dll
    if (-not $DryRun) { Copy-Item -Path ($libDll.FullName) -Destination $rootSp -Force }
    Write-Host ("      从总插件库并入 " + $libDll.Count + " 个 dll") -ForegroundColor DarkGray
}
foreach ($s in $servers) {
    $sp = Join-Path $s.FullName 'ServerPlugins'
    if (-not (Test-Path -LiteralPath $sp)) { continue }
    $tapi = Get-ChildItem -LiteralPath $sp -File | Where-Object { $_.Name -match '^TShockAPI' }
    if ($tapi) { if (-not $DryRun) { Copy-Item -Path ($tapi.FullName) -Destination $rootSp -Force }; Write-Host ("      TShockAPI.* 取自 " + $s.Name) -ForegroundColor DarkGray; break }
}
if (-not $DryRun) { Write-Host ("      根 ServerPlugins 共 " + (Get-ChildItem -LiteralPath $rootSp -File | Measure-Object).Count + " 个文件") -ForegroundColor Green }

Write-Host "[3/5] 各服瘦身（运行时与插件目录移入归档）" -ForegroundColor Cyan
foreach ($s in $servers) {
    Write-Host ("    · " + $s.Name) -ForegroundColor White
    foreach ($f in $runtimeFiles) { Move-ToArchive (Join-Path $s.FullName $f) $s.Name }
    foreach ($d in $runtimeDirs) { Move-ToArchive (Join-Path $s.FullName $d) $s.Name }
    foreach ($d in @('ServerPlugins', 'ServerPlugins.disabled')) { Move-ToArchive (Join-Path $s.FullName $d) $s.Name }
}

Write-Host "[4/5] 清理冗余的 Plugins 总插件库（内容已并入 ServerPlugins）" -ForegroundColor Cyan
Move-ToArchive $lib '_Plugins总插件库'

Write-Host "[5/5] 更新每服 config.json 与管理器 config.json" -ForegroundColor Cyan
foreach ($s in $servers) {
    $cfgFile = Join-Path $s.FullName 'config.json'
    if (-not (Test-Path -LiteralPath $cfgFile)) { continue }
    $m = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json
    $m.'总插件库' = ''
    $m.'覆盖插件目录' = $false
    $m.'备注' = ($m.'备注' -replace '（待迁移到 1\.PigeonServers）', '')
    if ($m.PSObject.Properties.Name -notcontains '说明') {
        $m | Add-Member -NotePropertyName '说明' -NotePropertyValue '插件由 PGame-TSManager 根目录的 ServerPlugins 统一提供（三服共用）；本清单用于自检该服的插件是否齐备。'
    }
    if (-not $DryRun) { [System.IO.File]::WriteAllText($cfgFile, ($m | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false))) }
    Write-Host ("      " + $s.Name) -ForegroundColor DarkGray
}
$mgrCfg = Join-Path $ManagerDir 'config.json'
if (Test-Path -LiteralPath $mgrCfg) {
    $c = Get-Content -Raw -Encoding UTF8 $mgrCfg | ConvertFrom-Json
    $c.pluginLibrary = 'ServerPlugins'
    $c.syncPluginsOnStart = $false
    $c.prunePlugins = $false
    if (-not $DryRun) { [System.IO.File]::WriteAllText($mgrCfg, ($c | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false))) }
    Write-Host "      管理器 config.json 已更新（pluginLibrary=ServerPlugins, 不同步/不裁剪）" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "完成。归档目录: $archive" -ForegroundColor Green
Write-Host "现在 PGame-TSManager 根目录 = 一个 TShock；1.PigeonServers 下只剩各服配置与世界。"