$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Administrator\Desktop\PGame-TSManager'
$incoming = Join-Path $root 'maintenance\incoming\build-06a22bf'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $root ('Backups\manager-update-' + $stamp)
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$manager = Get-Process -Name 'PGame-TSManager' -ErrorAction SilentlyContinue
if ($manager) { $manager | Stop-Process -Force }
Start-Sleep -Seconds 4
$children = Get-Process -Name 'TShock.Server' -ErrorAction SilentlyContinue
if ($children) { $children | Stop-Process -Force }
Start-Sleep -Seconds 5

foreach ($name in @(
    'PGame-TSManager.exe',
    'PGame-TSManager.dll',
    'PGame-TSManager.deps.json',
    'PGame-TSManager.runtimeconfig.json'
)) {
    $target = Join-Path $root $name
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name) -Force }
    Copy-Item -LiteralPath (Join-Path $incoming $name) -Destination $target -Force
}

& schtasks.exe /Run /TN PGameTSManager_Start | Out-Null
Write-Output ('Updated and start task requested. Backup: ' + $backup)
