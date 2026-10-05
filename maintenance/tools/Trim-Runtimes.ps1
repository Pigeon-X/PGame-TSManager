<#
  精简 runtimes\：只保留 Windows 平台的原生 SQLite。
  runtimes 是插件依赖 Microsoft.Data.Sqlite / SQLitePCLRaw.provider.e_sqlite3 带的原生库目录，
  里面每个平台一份 e_sqlite3；Windows 服务器只需要 win-*，其余（linux/osx/wasm）纯属死重量。
#>
[CmdletBinding()]
param([string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager')
$rt = Join-Path $ManagerDir 'runtimes'
if (-not (Test-Path -LiteralPath $rt)) { Write-Host "没有 runtimes 目录"; exit 0 }
$keep = @('win-x64','win-x86','win-arm64','win-arm')
$before = (Get-ChildItem -LiteralPath $rt -Recurse -File | Measure-Object Length -Sum).Sum
foreach ($d in (Get-ChildItem -LiteralPath $rt -Directory)) {
    if ($keep -contains $d.Name) { continue }
    Remove-Item -LiteralPath $d.FullName -Recurse -Force
    Write-Host ("  删除 " + $d.Name)
}
$after = (Get-ChildItem -LiteralPath $rt -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("完成：{0:N1} MB -> {1:N1} MB" -f ($before/1MB), ($after/1MB)) -ForegroundColor Green