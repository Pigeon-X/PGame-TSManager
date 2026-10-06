[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ManagerDir = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$PackagePath,
    [switch]$Apply,
    [switch]$SkipRuntimeCheck
)
$ErrorActionPreference = 'Stop'
$manager = (Resolve-Path -LiteralPath $ManagerDir).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stage = Join-Path $manager ('maintenance\staging\tshock-' + $stamp)
$backup = Join-Path $manager ('Backups\tshock-update-' + $stamp)
New-Item -ItemType Directory -Force -Path $stage, $backup | Out-Null

function Fail([string]$message) { throw "TShock update validation failed: $message" }
if (-not $SkipRuntimeCheck) {
    $running = Get-Process -Name 'TShock.Server' -ErrorAction SilentlyContinue
    if ($running) { Fail '检测到 TShock.Server 正在运行。先由 PGame-TSManager 停止三服，再执行 Apply。' }
}
if (-not $PackagePath) {
    Fail 'Provide a downloaded TShock archive from Pryaxis/TShock Releases or Actions.'
}
$package = (Resolve-Path -LiteralPath $PackagePath).Path
Expand-Archive -LiteralPath $package -DestinationPath $stage -Force
$server = Get-ChildItem -LiteralPath $stage -Filter 'TShock.Server.exe' -Recurse -File | Select-Object -First 1
if (-not $server) { Fail 'TShock.Server.exe is missing from the archive.' }
$api = Get-ChildItem -LiteralPath $stage -Filter 'TShockAPI.dll' -Recurse -File | Select-Object -First 1
if (-not $api) { Fail 'TShockAPI.dll is missing from the archive.' }
$serverRoot = $server.Directory.FullName

# 核心 DLL、依赖 DLL 和本地化资源必须来自同一包，先完整列出将要替换的文件。
$payload = Get-ChildItem -LiteralPath $serverRoot -File | Where-Object {
    $_.Name -in @('TShock.Server.exe','TShockAPI.dll') -or
    $_.Extension -in @('.dll','.json') -or $_.Name -in @('GeoIP.dat')
}
if (-not $payload) { Fail 'No usable runtime files were found in the archive.' }

foreach ($source in $payload) {
    $relative = $source.FullName.Substring($serverRoot.Length).TrimStart('\', '/')
    $target = Join-Path $manager $relative
    Write-Host "[预览] 将替换：$target"
    if (-not $Apply) { continue }
    if (Test-Path -LiteralPath $target) {
        $backupTarget = Join-Path $backup $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $backupTarget -Parent) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backupTarget -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    if ($PSCmdlet.ShouldProcess($target, "替换为 staging 的 $relative")) {
        $tmp = $target + '.pgame-new'
        Copy-Item -LiteralPath $source.FullName -Destination $tmp -Force
        Move-Item -LiteralPath $tmp -Destination $target -Force
    }
}
Write-Host "TShock archive validation complete. Staging: $stage"
if (-not $Apply) { Write-Host 'Preview mode: confirm shutdown, backup and source, then use -Apply to replace files.' }
else { Write-Host 'Core files replaced. Run --selfcheck, reapply the translation mapping and start servers in stages.' }
