<#
  清理 PGame-TSManager 下与运行无关的文件与备份。
  只删明确列出的类别，绝不碰：世界、tshock 配置、插件数据、ServerPlugins 总库、程序本体、Logs。
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [switch]$WhatIfOnly
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManagerDir)) { throw "找不到 $ManagerDir" }
$ManagerDir = (Resolve-Path -LiteralPath $ManagerDir).Path

$deletedDirs = New-Object System.Collections.Generic.List[string]
$deletedFiles = New-Object System.Collections.Generic.List[string]
$freed = 0L

function Get-Size([string]$p) {
    if (Test-Path -LiteralPath $p -PathType Container) {
        return (Get-ChildItem -LiteralPath $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    }
    return (Get-Item -LiteralPath $p -ErrorAction SilentlyContinue).Length
}
function Remove-ItemSafe([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return }
    $size = Get-Size $p
    if ($WhatIfOnly) { Write-Host ("  [试运行] " + $p); return }
    try {
        Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
        $script:freed += $size
    } catch {
        Write-Host ("  [跳过] " + $p + " : " + $_.Exception.Message) -ForegroundColor Yellow
        return
    }
    if (Test-Path -LiteralPath $p -PathType Container) { $deletedDirs.Add($p) } else { $deletedFiles.Add($p) }
    Write-Host ("  已删除: " + $p) -ForegroundColor DarkGray
}

Write-Host "=== 1. 根目录：迁移归档 / 生成物 / 启动 bat ===" -ForegroundColor Cyan
foreach ($pat in @('_归档_*')) {
    Get-ChildItem -LiteralPath $ManagerDir -Directory -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-ItemSafe $_.FullName }
}
foreach ($f in @('syncplugins.txt','selfcheck.txt','1.启动管理器.bat','2.自检.bat')) {
    Remove-ItemSafe (Join-Path $ManagerDir $f)
}

Write-Host "=== 2. 各服：插件备份 / 旧启动脚本 / 旧日志 ===" -ForegroundColor Cyan
$serversDir = Join-Path $ManagerDir '1.PigeonServers'
foreach ($s in (Get-ChildItem -LiteralPath $serversDir -Directory -ErrorAction SilentlyContinue)) {
    Write-Host ("  · " + $s.Name) -ForegroundColor White
    foreach ($pat in @('ServerPlugins.disabled','_*备份_*')) {
        Get-ChildItem -LiteralPath $s.FullName -Directory -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-ItemSafe $_.FullName }
    }
    foreach ($pat in @('*.bat','*.cmd','*.bak','*.bak-*','ServerLog.txt')) {
        Get-ChildItem -LiteralPath $s.FullName -File -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-ItemSafe $_.FullName }
    }
}

Write-Host ""
if ($WhatIfOnly) { Write-Host "（试运行，未删除任何文件）" -ForegroundColor Yellow; exit 0 }
Write-Host ("完成：删除目录 {0} 个、文件 {1} 个，释放约 {2:N1} MB" -f $deletedDirs.Count, $deletedFiles.Count, ($freed/1MB)) -ForegroundColor Green