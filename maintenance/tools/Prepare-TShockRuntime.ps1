[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$PackagePath,
    [string]$TShockApiPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime\TShockAPI.dll'),
    [string]$HotReloadPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime\HotReload.dll'),
    [string]$ReleaseRepository = 'Pryaxis/TShock'
)

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('pgame-tshock-runtime-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    if (-not $PackagePath) {
        $release = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/$ReleaseRepository/releases/latest" `
            -Headers @{ 'User-Agent' = 'PGame-TSManager' } `
            -TimeoutSec 60
        $asset = $release.assets |
            Where-Object { $_.name -match 'win-x64.*Release\.zip$' } |
            Select-Object -First 1
        if (-not $asset) {
            throw "未找到 TShock Windows x64 发布资产。Release: $($release.tag_name)"
        }
        $PackagePath = Join-Path $tempRoot $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $PackagePath -TimeoutSec 300
        Write-Host "下载 TShock：$($release.tag_name) / $($asset.name)" -ForegroundColor Cyan
    }

    $package = (Resolve-Path -LiteralPath $PackagePath).Path
    $extract = Join-Path $tempRoot 'extract'
    Expand-Archive -LiteralPath $package -DestinationPath $extract -Force

    $server = Get-ChildItem -LiteralPath $extract -Filter 'TShock.Server.exe' -Recurse -File |
        Select-Object -First 1
    if (-not $server) { throw 'TShock 包中缺少 TShock.Server.exe。' }
    $runtimeRoot = $server.Directory.FullName

    Get-ChildItem -LiteralPath $runtimeRoot -Force |
        Where-Object { $_.Name -ne 'TShock.Installer.exe' } |
        ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $output -Recurse -Force
        }

    if (-not (Test-Path -LiteralPath $TShockApiPath -PathType Leaf)) {
        throw "缺少项目汉化 TShockAPI.dll：$TShockApiPath"
    }
    $serverPlugins = Join-Path $output 'ServerPlugins'
    [IO.Directory]::CreateDirectory($serverPlugins) | Out-Null
    Copy-Item -LiteralPath $TShockApiPath -Destination (Join-Path $serverPlugins 'TShockAPI.dll') -Force
    if (-not (Test-Path -LiteralPath $HotReloadPath -PathType Leaf)) {
        throw "缺少 HotReload.dll：$HotReloadPath"
    }
    Copy-Item -LiteralPath $HotReloadPath -Destination (Join-Path $serverPlugins 'HotReload.dll') -Force

    Write-Host "TShock Core 运行时已准备：$output" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
