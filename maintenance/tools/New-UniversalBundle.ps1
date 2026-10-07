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
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-universal-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

try {
    Copy-DirectoryContents $publish $stage
    foreach ($relative in @('maintenance\tools', 'maintenance\templates', 'README.md')) {
        $source = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $destination = Join-Path $stage $relative
        if ((Get-Item -LiteralPath $source).PSIsContainer) {
            Copy-DirectoryContents $source $destination
        } else {
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }
    $privateMigrationTool = Join-Path $stage 'maintenance\tools\New-PigeonServersLayout.ps1'
    if (Test-Path -LiteralPath $privateMigrationTool) {
        Remove-Item -LiteralPath $privateMigrationTool -Force
    }
    & (Join-Path $root 'maintenance\tools\Migrate-ManagerLayout.ps1') `
        -ManagerDir $stage -IncludeSourceDirectories
    & (Join-Path $root 'maintenance\tools\Add-TShockTemplates.ps1') `
        -ManagerDir $stage
    $parent = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if ($files.Count -eq 0) { throw 'No universal package files selected.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "Universal bundle created: $OutputPath" -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
