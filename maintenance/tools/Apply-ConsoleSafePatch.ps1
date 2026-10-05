<#
  A 方案：给 Plugins\ 里的插件 DLL 批量打「Console 安全」补丁。
  做法：复制副本 -> 用 TShockConsoleColorPatch 打补丁 -> 改名替换（绕过文件占用）。
  被替换的原文件保留为 <名字>.pre-console-safe
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [string]$PatchTool  = '',
    [string[]]$Targets  = @('TShockAPI.dll','TsWeb.dll','CustomPlayer.dll','StatusTextManager.dll')
)
$ErrorActionPreference = 'Stop'
if (-not $PatchTool) {
    $PatchTool = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\TShockConsoleColorPatch\bin\Release\net9.0\TShockConsoleColorPatch.dll')).Path
}
$pool = Join-Path $ManagerDir 'Plugins'
$tmpDir = Join-Path $env:TEMP ('console-safe-' + (Get-Date -Format 'HHmmss'))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

$work = @()
foreach ($name in $Targets) {
    $src = Join-Path $pool $name
    if (-not (Test-Path -LiteralPath $src)) { Write-Host ("  跳过（不存在）: " + $name) -ForegroundColor Yellow; continue }
    $copy = Join-Path $tmpDir $name
    Copy-Item -LiteralPath $src -Destination $copy -Force
    $work += [pscustomobject]@{ Name = $name; Src = $src; Copy = $copy }
}

Write-Host "[1/2] 打补丁（在副本上）" -ForegroundColor Cyan
$paths = $work | ForEach-Object { $_.Copy }
if ($paths.Count -gt 0) { & dotnet $PatchTool @paths }

Write-Host "[2/2] 改名替换" -ForegroundColor Cyan
foreach ($w in $work) {
    $bak = $w.Src + '.pre-console-safe'
    try {
        if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force }
        Move-Item -LiteralPath $w.Src   -Destination $bak -ErrorAction Stop
        Move-Item -LiteralPath $w.Copy  -Destination $w.Src -ErrorAction Stop
        Write-Host ("  {0} 已替换（原文件 -> {1}）" -f $w.Name, (Split-Path -Leaf $bak)) -ForegroundColor Green
    } catch {
        Write-Host ("  {0} 替换失败: {1}" -f $w.Name, $_.Exception.Message) -ForegroundColor Red
        if ((Test-Path -LiteralPath $bak) -and -not (Test-Path -LiteralPath $w.Src)) { Move-Item -LiteralPath $bak -Destination $w.Src }
    }
}
Write-Host "完成。" -ForegroundColor Green