[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoreRoot,
    [Parameter(Mandatory = $true)][string]$OverlayRoot,
    [string]$ManifestName = 'overlay.json',
    [switch]$AllowManagerConfigOverwrite
)

$ErrorActionPreference = 'Stop'
$core = (Resolve-Path -LiteralPath $CoreRoot).Path.TrimEnd('\')
$overlay = (Resolve-Path -LiteralPath $OverlayRoot).Path.TrimEnd('\')
$manifestPath = Join-Path $overlay $ManifestName
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Overlay manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$mappings = @{
    plugins = 'Plugins'
    pluginDependencies = 'bin'
    data = 'Data'
    servers = '1.PigeonServers'
    rpgConfigs = 'RPGConfigs'
    botIntegration = 'PigeonBot'
    tools = 'Tools'
}

foreach ($key in $mappings.Keys) {
    $source = Join-Path $overlay $key
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { continue }
    $destination = Join-Path $core $mappings[$key]
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $source '*') -Destination $destination -Recurse -Force
    Write-Host "Overlay: $key -> $destination" -ForegroundColor DarkCyan
}

$managerConfig = [string]$manifest.managerConfig
if (-not [string]::IsNullOrWhiteSpace($managerConfig)) {
    $source = Join-Path $overlay $managerConfig
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $target = Join-Path $core 'config.json'
        if ((Test-Path -LiteralPath $target) -and -not $AllowManagerConfigOverwrite) {
            Copy-Item -LiteralPath $target -Destination ($target + '.core') -Force
        }
        Copy-Item -LiteralPath $source -Destination $target -Force
        Write-Host "Overlay: manager config -> $target" -ForegroundColor DarkCyan
    }
}

$metadata = [ordered]@{
    overlayName = [string]$manifest.name
    coreVersion = [string]$manifest.coreVersion
    appliedAt = (Get-Date).ToString('s')
    coreRoot = $core
    overlayRoot = $overlay
}
$metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $core 'personal-overlay.json') -Encoding UTF8
Write-Host 'Personal overlay applied.' -ForegroundColor Green
