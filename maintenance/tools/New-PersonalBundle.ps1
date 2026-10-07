[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OverlayRoot,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$overlay = (Resolve-Path -LiteralPath $OverlayRoot).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-personal-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $publish '*') -Destination $stage -Recurse -Force
    & (Join-Path $root 'maintenance\tools\Apply-PersonalOverlay.ps1') `
        -CoreRoot $stage -OverlayRoot $overlay -AllowManagerConfigOverwrite
    $parent = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if ($files.Count -eq 0) { throw 'No personal package files selected.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "Personal bundle created: $OutputPath" -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
