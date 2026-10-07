[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-universal-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $publish '*') -Destination $stage -Recurse -Force
    foreach ($relative in @('maintenance', 'README.md')) {
        $source = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $destination = Join-Path $stage $relative
        if ((Get-Item -LiteralPath $source).PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $destination | Out-Null
            Copy-Item -LiteralPath (Join-Path $source '*') -Destination $destination -Recurse -Force
        } else {
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }
    $parent = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if ($files.Count -eq 0) { throw 'No universal package files selected.' }
    Compress-Archive -LiteralPath $files.FullName -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Universal bundle created: $OutputPath" -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
