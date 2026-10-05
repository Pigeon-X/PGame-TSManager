<#
  清理 PGame-TSManager 的残余与备份文件（脱离旧服务器维护方式）。
  只删明确列出的类别，绝不碰：世界、tshock 配置、Plugins 插件总库、bin 依赖、程序本体。
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManagerDir)) { throw "找不到 $ManagerDir" }
$ManagerDir = (Resolve-Path -LiteralPath $ManagerDir).Path

$deleted = 0
$freed = 0L
function Remove-Safe([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return }
    $size = if (Test-Path -LiteralPath $p -PathType Container) {
        (Get-ChildItem -LiteralPath $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    } else { (Get-Item -LiteralPath $p -ErrorAction SilentlyContinue).Length }
    if ($DryRun) { Write-Host ("  [试运行] " + $p); return }
    try { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop } catch { Write-Host ("  [跳过] " + $p) -ForegroundColor Yellow; return }
    $script:deleted++; $script:freed += $size
    Write-Host ("  已删除: " + $p) -ForegroundColor DarkGray
}

Write-Host "=== 1. 根目录：旧归档 / 生成物 / 崩溃日志 ===" -ForegroundColor Cyan
foreach ($pat in @('_归档_*','Backups')) {
    Get-ChildItem -LiteralPath $ManagerDir -Directory -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-Safe $_.FullName }
}
foreach ($f in @('syncplugins.txt','selfcheck.txt','crash.log','syncplugins.txt')) {
    Remove-Safe (Join-Path $ManagerDir $f)
}
foreach ($pat in @('*.bak','*.bak-*','*.orig')) {
    Get-ChildItem -LiteralPath $ManagerDir -File -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-Safe $_.FullName }
}

Write-Host "=== 2. 各服目录：只提示多余项，不自动删除 ===" -ForegroundColor Cyan
$serversRoot = Join-Path $ManagerDir '1.PigeonServers'
foreach ($s in (Get-ChildItem -LiteralPath $serversRoot -Directory -ErrorAction SilentlyContinue)) {
    $extra = @(Get-ChildItem -LiteralPath $s.FullName -Force | Where-Object { $_.Name -notin @('config.json','tshock') })
    if ($extra.Count -gt 0) {
        Write-Host ("  · " + $s.Name + " 有多余项（请人工确认）:") -ForegroundColor Yellow
        $extra | ForEach-Object { Write-Host ("      " + $_.Name) }
    }
}

Write-Host "=== 3. 运行沙箱：插件停用备份 / 日志 ===" -ForegroundColor Cyan
$rt = Join-Path $ManagerDir '_runtime'
foreach ($d in (Get-ChildItem -LiteralPath $rt -Directory -ErrorAction SilentlyContinue)) {
    foreach ($pat in @('ServerPlugins.disabled')) {
        Get-ChildItem -LiteralPath $d.FullName -Directory -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object { Remove-Safe $_.FullName }
    }
    foreach ($f in @('console.log','ServerLog.txt')) {
        Remove-Safe (Join-Path $d.FullName $f)
    }
    $logs = Join-Path $d.FullName 'Logs'
    if (Test-Path -LiteralPath $logs) { Get-ChildItem -LiteralPath $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Remove-Safe $_.FullName } }
}

Write-Host ""
if ($DryRun) { Write-Host "（试运行，未删除）" -ForegroundColor Yellow; exit 0 }
Write-Host ("完成：删除 {0} 项，释放约 {1:N1} MB" -f $deleted, ($freed/1MB)) -ForegroundColor Green