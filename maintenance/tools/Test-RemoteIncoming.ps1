$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = 'C:\Users\Administrator\Desktop\PGame-TSManager'
$incoming = Join-Path $root 'maintenance\incoming\4213179'
$files = Get-ChildItem -LiteralPath $incoming -Recurse -File -Filter '*.ps1'
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw ($file.Name + ': ' + $parseErrors[0].Message) }
}
Write-Output ('PowerShell syntax passed: ' + $files.Count + ' scripts')
$layoutScript = Join-Path $incoming 'tools\Test-RepositoryLayout.ps1'
& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $layoutScript -RepositoryRoot $root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'Remote incoming maintenance validation passed.'
