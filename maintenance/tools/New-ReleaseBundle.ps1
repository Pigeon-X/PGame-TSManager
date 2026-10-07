[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$OutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath)) }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

try {
    Copy-DirectoryContents $publish $stage
    foreach ($relative in @('maintenance', 'README.md')) {
        $source = Join-Path $root $relative
        if (Test-Path -LiteralPath $source) {
            $destination = Join-Path $stage $relative
            if ((Get-Item -LiteralPath $source).PSIsContainer) {
                Copy-DirectoryContents $source $destination
            } else {
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
        }
    }
    $template = [ordered]@{
        backupBeforeStart = $true
        backupDir = 'Core\Backups'
        backupKeep = 10
        serverDir = 'Servers\Profiles'
        pluginDir = 'Plugins'
        runtimeDir = 'Core\_runtime'
        sharedRuntimeDir = 'Core'
        worldDir = 'Servers\Worlds'
        logDir = 'Core\Logs'
        dataDir = 'Core\Data'
        toolsDir = 'Tools'
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
    & (Join-Path $root 'maintenance\tools\Migrate-ManagerLayout.ps1') `
        -ManagerDir $stage -IncludeSourceDirectories
    $parent = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    $archiveFiles = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if ($archiveFiles.Count -eq 0) { throw 'No release files selected for the bundle.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) { throw "Archive was not created: $OutputPath" }
    Write-Host "Release template created: $OutputPath"
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
