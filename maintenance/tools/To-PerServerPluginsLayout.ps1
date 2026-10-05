<#
  旧版 TS 管理器模型：总库 + 每服独立插件
    - 根目录 ServerPlugins = 插件总库（统一维护的唯一来源）
    - 每服 config.json 的「插件」清单决定该服加载哪些插件（含 TShockAPI）
    - 每服 ServerPlugins = 由管理器启动时按 config.json 从总库同步而来
    - 运行时（TShock.Server.exe / bin / i18n / runtimes / x64 / GeoIP.dat）只保留根目录一份，
      各服用 硬链接/目录联接 引用，不重复占空间
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager'
)
$ErrorActionPreference = 'Stop'
$serversDir = Join-Path $ManagerDir '1.PigeonServers'
if (-not (Test-Path -LiteralPath $serversDir)) { throw "找不到 $serversDir" }

function Ensure-FileLink([string]$name) {
    $master = Join-Path $ManagerDir $name
    if (-not (Test-Path -LiteralPath $master)) { return }
    foreach ($s in (Get-ChildItem -LiteralPath $serversDir -Directory)) {
        $link = Join-Path $s.FullName $name
        if (Test-Path -LiteralPath $link) { continue }
        try {
            New-Item -ItemType HardLink -Path $link -Target $master -ErrorAction Stop | Out-Null
            Write-Host ("      {0}\{1} 硬链接" -f $s.Name, $name) -ForegroundColor DarkGray
        } catch {
            Copy-Item -LiteralPath $master -Destination $link -Force
            Write-Host ("      {0}\{1} 复制（硬链接不可用）" -f $s.Name, $name) -ForegroundColor Yellow
        }
    }
}

function Ensure-DirLink([string]$name) {
    $master = Join-Path $ManagerDir $name
    if (-not (Test-Path -LiteralPath $master)) { return }
    foreach ($s in (Get-ChildItem -LiteralPath $serversDir -Directory)) {
        $link = Join-Path $s.FullName $name
        if (Test-Path -LiteralPath $link) { continue }
        try {
            New-Item -ItemType Junction -Path $link -Target $master -ErrorAction Stop | Out-Null
            Write-Host ("      {0}\{1}\ 目录联接" -f $s.Name, $name) -ForegroundColor DarkGray
        } catch {
            Copy-Item -LiteralPath $master -Destination $link -Recurse -Force
            Write-Host ("      {0}\{1}\ 复制（联接不可用）" -f $s.Name, $name) -ForegroundColor Yellow
        }
    }
}

Write-Host "[1/3] 为各服建立运行时引用（不重复占空间）" -ForegroundColor Cyan
Ensure-FileLink 'TShock.Server.exe'
Ensure-FileLink 'GeoIP.dat'
foreach ($d in @('bin','i18n','runtimes','x64')) { Ensure-DirLink $d }

Write-Host "[2/3] 建立各服 ServerPlugins（实体目录，由 config.json 决定内容）" -ForegroundColor Cyan
foreach ($s in (Get-ChildItem -LiteralPath $serversDir -Directory)) {
    $sp = Join-Path $s.FullName 'ServerPlugins'
    if (Test-Path -LiteralPath $sp) {
        $item = Get-Item -LiteralPath $sp -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            cmd /c rmdir "`"$sp`"" | Out-Null
            New-Item -ItemType Directory -Force -Path $sp | Out-Null
            Write-Host ("      {0}\ServerPlugins 由联接改为实体目录" -f $s.Name) -ForegroundColor DarkGray
        }
    } else {
        New-Item -ItemType Directory -Force -Path $sp | Out-Null
        Write-Host ("      {0}\ServerPlugins 已建立" -f $s.Name) -ForegroundColor DarkGray
    }
}

Write-Host "[3/3] 保证每服 config.json 的插件清单含 TShockAPI.*" -ForegroundColor Cyan
foreach ($s in (Get-ChildItem -LiteralPath $serversDir -Directory)) {
    $cfgFile = Join-Path $s.FullName 'config.json'
    if (-not (Test-Path -LiteralPath $cfgFile)) { Write-Host ("      跳过（无 config.json）: " + $s.Name) -ForegroundColor Yellow; continue }
    $m = Get-Content -Raw -Encoding UTF8 $cfgFile | ConvertFrom-Json
    $plugins = @($m.'插件')
    if ($plugins -notcontains 'TShockAPI.dll') { $plugins = @('TShockAPI.dll') + $plugins }
    $m.'插件' = @($plugins | Select-Object -Unique)
    $m.'总插件库' = '..\..\ServerPlugins'
    $m.'覆盖插件目录' = $true
    [System.IO.File]::WriteAllText($cfgFile, ($m | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("      {0}: {1} 个插件" -f $s.Name, @($m.'插件').Count) -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "完成：根 ServerPlugins = 插件总库；各服 ServerPlugins 由各自 config.json 决定。" -ForegroundColor Green