[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$OutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath)) }
$parent = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $parent | Out-Null

$exclude = @(
    '.git', '.vs', 'bin', 'obj', 'artifacts',
    '_runtime', 'Worlds', 'Plugins', 'ServerPlugins', 'Backups',
    '1.PigeonServers', 'Logs', 'crash.log', 'selfcheck.txt', 'syncplugins.txt',
    '*.dll', '*.exe', '*.pdb', '*.wld', '*.db', '*.sqlite', '*.zip'
)

$files = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($root.Length).TrimStart([char[]]@('\', '/'))
    $parts = $relative -split '[\\/]'
    $blocked = $false
    foreach ($pattern in $exclude) {
        if ($pattern.Contains('*')) {
            if ($_.Name -like $pattern) { $blocked = $true; break }
        } elseif ($parts -contains $pattern -or $_.Name -eq $pattern) {
            $blocked = $true; break
        }
    }
    -not $blocked
}

if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PGame-TSManager-source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length).TrimStart([char[]]@('\', '/'))
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    Compress-Archive -LiteralPath (Join-Path $stage '*') -DestinationPath $OutputPath -CompressionLevel Optimal
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "Source bundle created: $OutputPath"
