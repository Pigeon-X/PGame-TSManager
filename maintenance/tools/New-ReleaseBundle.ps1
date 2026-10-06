[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$OutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath)) }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $publish '*') -Destination $stage -Recurse -Force
    foreach ($relative in @('maintenance', 'README.md')) {
        $source = Join-Path $root $relative
        if (Test-Path -LiteralPath $source) {
            $destination = Join-Path $stage $relative
            if ((Get-Item -LiteralPath $source).PSIsContainer) {
                New-Item -ItemType Directory -Force -Path $destination | Out-Null
                Copy-Item -LiteralPath (Join-Path $source '*') -Destination $destination -Recurse -Force
            } else {
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
        }
    }
    $template = [ordered]@{
        backupBeforeStart = $true
        backupDir = 'Backups'
        backupKeep = 10
        serverDir = '1.PigeonServers'
        pluginDir = 'Plugins'
        runtimeDir = '_runtime'
        startAllSequential = $true
        watchdogEnabled = $true
        alertEnabled = $true
        serverProfiles = @()
    }
    $template | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'config.json') -Encoding UTF8
    $readme = Join-Path $stage '部署说明.txt'
    @'
PGame-TSManager release template

This package contains the manager, maintenance tools and an empty configuration template.
Place TShock.Server.exe, bin, i18n, runtimes, GeoIP.dat and plugin dependencies in their directories,
then configure servers according to the maintenance documentation.
Running servers, worlds, databases, logs, _runtime, Plugins and ServerPlugins are excluded from GitHub bundles.
'@ | Set-Content -LiteralPath $readme -Encoding UTF8
    $parent = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    Compress-Archive -LiteralPath (Join-Path $stage '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Release template created: $OutputPath"
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
