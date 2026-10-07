[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$ArchivePath,
    [Parameter(Mandatory = $true)][string[]]$FilePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($archive in $ArchivePath) {
    $resolvedArchive = (Resolve-Path -LiteralPath $archive).Path
    $zip = [IO.Compression.ZipFile]::Open($resolvedArchive, [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($file in $FilePath) {
            $resolvedFile = (Resolve-Path -LiteralPath $file).Path
            $entryName = [IO.Path]::GetFileName($resolvedFile)
            $existing = $zip.GetEntry($entryName)
            if ($null -ne $existing) { $existing.Delete() }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $resolvedFile,
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal
            )
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "Updated archive: $resolvedArchive" -ForegroundColor Green
}
