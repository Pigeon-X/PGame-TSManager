<#
  把三个服归类到 PGame-TSManager，并建立「总插件库」。
  目标结构：
    PGame-TSManager\
      配置文件/exe、maintenance\、Backups\
      Plugins\                        总插件库（三服共用，唯一来源）
      1.PigeonServers\
        流光城\   config.json(TSM每服配置) + ServerPlugins\ + tshock\ + TShock.Server.exe ...
        泰拉大陆\
        流光神域\
  服务器默认“移动”进新结构（-Copy 可改为复制，原目录保留）。
#>
[CmdletBinding()]
param(
    [string]$ManagerDir = 'D:\59934\Desktop\PGame-TSManager',
    [string]$SourceRoot = 'D:\59934\Desktop\流光服',
    [switch]$Copy,
    [switch]$SkipServers,
    [switch]$SkipManagerMove
)
$ErrorActionPreference = 'Stop'

$servers = @(
    [pscustomobject]@{ Name = '流光城';   Src = (Join-Path $SourceRoot '1.流光城') },
    [pscustomobject]@{ Name = '泰拉大陆'; Src = (Join-Path $SourceRoot '2.RPG\1.泰拉大陆') },
    [pscustomobject]@{ Name = '流光神域'; Src = (Join-Path $SourceRoot '2.RPG\2.流光神域') }
)

Write-Host "[1/7] 准备目录 $ManagerDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $ManagerDir | Out-Null
$serversDir = Join-Path $ManagerDir '1.PigeonServers'
New-Item -ItemType Directory -Force -Path $serversDir | Out-Null
$libraryDir = Join-Path $ManagerDir 'Plugins'
New-Item -ItemType Directory -Force -Path $libraryDir | Out-Null

Write-Host "[2/7] 迁移管理器程序文件" -ForegroundColor Cyan
$oldManager = Join-Path $SourceRoot 'PGame-TSManager'
if ((-not $SkipManagerMove) -and (Test-Path -LiteralPath $oldManager) -and ((Resolve-Path -LiteralPath $oldManager).Path -ne (Resolve-Path -LiteralPath $ManagerDir).Path)) {
    Get-ChildItem -LiteralPath $oldManager -Force | ForEach-Object {
        $dest = Join-Path $ManagerDir $_.Name
        if (Test-Path -LiteralPath $dest) {
            Write-Host ("      跳过已存在: " + $_.Name) -ForegroundColor DarkGray
        } else {
            Move-Item -LiteralPath $_.FullName -Destination $dest
            Write-Host ("      移动: " + $_.Name) -ForegroundColor DarkGray
        }
    }
}

Write-Host "[3/7] 把三个服放入 1.PigeonServers" -ForegroundColor Cyan
$moved = @{}
foreach ($s in $servers) {
    $dest = Join-Path $serversDir $s.Name
    $moved[$s.Name] = $dest
    if (Test-Path -LiteralPath $dest) { Write-Host ("      已存在，跳过: " + $s.Name) -ForegroundColor Yellow; continue }
    if (-not (Test-Path -LiteralPath $s.Src)) { Write-Host ("      源不存在，跳过: " + $s.Src) -ForegroundColor Yellow; continue }
    if ($Copy) {
        Copy-Item -LiteralPath $s.Src -Destination $dest -Recurse
        Write-Host ("      复制: " + $s.Name) -ForegroundColor DarkGray
    } else {
        Move-Item -LiteralPath $s.Src -Destination $dest
        Write-Host ("      移动: " + $s.Name) -ForegroundColor DarkGray
    }
}

if (-not $SkipServers) {
    Write-Host "[4/7] 建立总插件库（同名取最新版，旧版归档 _alternates）" -ForegroundColor Cyan
    $isExcluded = {
        param($n)
        $n -match '^TShockAPI' -or $n -match '\.bak' -or $n -match '\.orig$'
    }
    $best = @{}   # fileName -> FileInfo
    foreach ($s in $servers) {
        $sp = Join-Path $moved[$s.Name] 'ServerPlugins'
        if (-not (Test-Path -LiteralPath $sp)) { continue }
        foreach ($f in (Get-ChildItem -LiteralPath $sp -File)) {
            if (& $isExcluded $f.Name) { continue }
            if (-not $best.ContainsKey($f.Name) -or $f.LastWriteTime -gt $best[$f.Name].LastWriteTime) {
                $best[$f.Name] = $f
            }
        }
    }
    foreach ($name in $best.Keys) {
        $src = $best[$name]
        $dest = Join-Path $libraryDir $name
        Copy-Item -LiteralPath $src.FullName -Destination $dest -Force
    }
    # 冲突归档
    foreach ($s in $servers) {
        $sp = Join-Path $moved[$s.Name] 'ServerPlugins'
        if (-not (Test-Path -LiteralPath $sp)) { continue }
        foreach ($f in (Get-ChildItem -LiteralPath $sp -File)) {
            if (& $isExcluded $f.Name) { continue }
            $lib = Join-Path $libraryDir $f.Name
            if (-not (Test-Path -LiteralPath $lib)) { continue }
            $a = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
            $b = (Get-FileHash -LiteralPath $lib -Algorithm SHA256).Hash
            if ($a -ne $b) {
                $altDir = Join-Path $libraryDir ("_alternates\" + $f.Name)
                New-Item -ItemType Directory -Force -Path $altDir | Out-Null
                $tag = ("{0}_{1}_{2}" -f $s.Name, $f.LastWriteTime.ToString('yyyyMMdd'), $f.Length)
                Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $altDir $tag) -Force
                Write-Host ("      归档旧版: {0} <- {1}" -f $f.Name, $s.Name) -ForegroundColor Yellow
            }
        }
    }
    Write-Host ("      总插件库文件数: " + (Get-ChildItem -LiteralPath $libraryDir -File).Count) -ForegroundColor Green

    Write-Host "[5/7] 写每服 config.json（TSM 配置：启动参数 + 插件清单）" -ForegroundColor Cyan
    $launch = @{
        '流光城'   = '-lang 7 -port 2021 -world "地图\1.PigeonGame.wld"'
        '泰拉大陆' = '-config server.properties -port 2023 -lang 7'
        '流光神域' = '-config server.properties -port 2024 -lang 7'
    }
    $remark = @{
        '流光城'   = '流光城 端口 2021 / REST 7878'
        '泰拉大陆' = '泰拉大陆 端口 2023 / REST 7879'
        '流光神域' = '流光神域 端口 2024 / REST 7880'
    }
    foreach ($s in $servers) {
        $srvDir = $moved[$s.Name]
        if (-not (Test-Path -LiteralPath $srvDir)) { continue }
        $sp = Join-Path $srvDir 'ServerPlugins'
        $dlls = @()
        if (Test-Path -LiteralPath $sp) {
            $dlls = Get-ChildItem -LiteralPath $sp -File -Filter *.dll |
                    Where-Object { $_.Name -notmatch '^TShockAPI' -and $_.Name -notmatch '\.bak' } |
                    Select-Object -ExpandProperty Name | Sort-Object
        }
        $manifest = [ordered]@{
            '服务器名称'   = $s.Name
            '启用'         = $true
            '启动参数'     = $launch[$s.Name]
            '总插件库'     = '..\..\Plugins'
            '覆盖插件目录' = $true
            '插件'         = @($dlls)
            '备注'         = $remark[$s.Name]
        }
        $json = $manifest | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText((Join-Path $srvDir 'config.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("      " + $s.Name + " -> " + $dlls.Count + " 个插件") -ForegroundColor DarkGray
    }
}
else {
    Write-Host "[4/7][5/7] 跳过插件库与每服配置" -ForegroundColor Yellow
}

Write-Host "[6/7] 写管理器 config.json（serverProfiles）" -ForegroundColor Cyan
$profiles = @()
foreach ($s in $servers) {
    $profiles += [ordered]@{
        name          = $s.Name
        rootPath      = (Join-Path $serversDir $s.Name)
        executable    = 'TShock.Server.exe'
        arguments     = ''
        enabled       = $true
        remark        = $remark[$s.Name]
        plugins       = @()
        pluginLibrary = ''
    }
}
$mgr = [ordered]@{
    backupBeforeStart  = $true
    backupDir          = 'Backups'
    backupKeep         = 10
    pluginLibrary      = 'Plugins'
    syncPluginsOnStart = $true
    prunePlugins       = $true
    disabledPluginDir  = 'ServerPlugins.disabled'
    serverProfiles     = $profiles
}
[System.IO.File]::WriteAllText((Join-Path $ManagerDir 'config.json'), ($mgr | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[7/7] 完成" -ForegroundColor Green
Write-Host ("管理器: " + $ManagerDir)
Write-Host ("服务器: " + $serversDir)
Write-Host ("总插件库: " + $libraryDir)