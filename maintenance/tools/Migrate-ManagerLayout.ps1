[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ManagerDir,
    [switch]$IncludeSourceDirectories,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ManagerDir).Path

function Ensure-Directory([string]$Path) {
    if ($DryRun) { return }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Move-Into([string]$Source, [string]$DestinationParent) {
    if (-not (Test-Path -LiteralPath $Source)) { return }
    Ensure-Directory $DestinationParent
    $destination = Join-Path $DestinationParent (Split-Path -Leaf $Source)
    if (Test-Path -LiteralPath $destination) {
        $sourceItem = Get-Item -LiteralPath $Source -Force
        if ($sourceItem.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $Source -Force) {
                Move-Into $child.FullName $destination
            }
            if (-not $DryRun) {
                $remaining = @(Get-ChildItem -LiteralPath $Source -Force)
                if ($remaining.Count -eq 0) { Remove-Item -LiteralPath $Source -Recurse -Force }
            }
        } else {
            if ($DryRun) {
                Write-Host "[dry-run] keep existing file and remove old copy: $destination"
            } else {
                Remove-Item -LiteralPath $Source -Force
            }
        }
        return
    }
    if ($DryRun) {
        Write-Host "[dry-run] $Source -> $destination"
        return
    }
    Move-Item -LiteralPath $Source -Destination $DestinationParent -Force
    Write-Host "Moved: $Source -> $destination"
}

function Set-Property([object]$Object, [string]$Name, [object]$Value) {
    if ($null -ne $Object.PSObject.Properties[$Name]) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }
}

$core = Join-Path $root 'Core'
$servers = Join-Path $root 'Servers'
$tools = Join-Path $root 'Tools'
$sourceArchive = Join-Path $tools 'Source'
$artifactsArchive = Join-Path $tools 'Artifacts'

Ensure-Directory $core
Ensure-Directory $servers
Ensure-Directory $tools

foreach ($name in @(
    'TShock.Server.exe',
    'GeoIP.dat',
    'bin',
    'i18n',
    'runtimes',
    'x64',
    'Data',
    'Logs',
    'Backups',
    '_runtime'
)) {
    Move-Into (Join-Path $root $name) $core
}

Ensure-Directory (Join-Path $servers 'Profiles')
Move-Into (Join-Path $root '1.PigeonServers') (Join-Path $servers 'Profiles')
Move-Into (Join-Path $root 'Worlds') $servers

Move-Into (Join-Path $root 'maintenance') $tools

if ($IncludeSourceDirectories) {
    foreach ($name in @('PGame-TSManager', 'TerrariaServerAPI', 'package-check', 'personal-overlay', '.github', '_tmp_pgame_package_test')) {
        Move-Into (Join-Path $root $name) $sourceArchive
    }
    Move-Into (Join-Path $root 'artifacts') $artifactsArchive
}

$configPath = Join-Path $root 'config.json'
if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Set-Property $config 'sharedRuntimeDir' 'Core'
    Set-Property $config 'worldDir' 'Servers\Worlds'
    Set-Property $config 'pluginDir' 'Plugins'
    Set-Property $config 'runtimeDir' 'Core\_runtime'
    Set-Property $config 'serverDir' 'Servers\Profiles'
    Set-Property $config 'backupDir' 'Core\Backups'
    Set-Property $config 'logDir' 'Core\Logs'
    Set-Property $config 'dataDir' 'Core\Data'
    Set-Property $config 'toolsDir' 'Tools'

    foreach ($profile in @($config.serverProfiles)) {
        if ($null -eq $profile) { continue }
        $profile.rootPath = [string]$profile.rootPath -replace '1\.PigeonServers', 'Servers\Profiles'
    }

    if ($DryRun) {
        Write-Host '[dry-run] config.json would be updated to Core/Servers/Plugins/Tools.'
    } else {
        $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath -Encoding UTF8
        Write-Host "Updated: $configPath"
    }
}

Write-Host 'Manager layout migration completed. Visible folders: Core, Servers, Plugins, Tools.' -ForegroundColor Green
