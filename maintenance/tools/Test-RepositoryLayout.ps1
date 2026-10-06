[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$bad = @()
$tracked = @(& git -C $root ls-files)
foreach ($path in $tracked) {
    $normalized = $path.Replace('/', '\')
    if ($normalized -match '^(1\.PigeonServers|_runtime|Worlds|Plugins|ServerPlugins|Backups|Logs)\\' -or
        $normalized -match '\.(exe|dll|pdb|wld|db|sqlite|zip)$' -or
        $normalized -match '(^|\\)(crash\.log|selfcheck\.txt|syncplugins\.txt)$') {
        $bad += $path
    }
}
if ($bad.Count -gt 0) {
    Write-Host 'Repository layout check failed: runtime files or data are tracked.' -ForegroundColor Red
    $bad | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Repository layout check passed: $($tracked.Count) tracked files; no runtime server data found." -ForegroundColor Green
