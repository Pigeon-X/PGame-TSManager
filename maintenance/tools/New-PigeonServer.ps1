<#
  新增一台受管服务器（PGame-TSManager）

  目录名自动取下一个序号：已有 1./2./3. → 新的是 4.名字，再加就是 5.名字 …
  管理器启动时会自动发现这些目录（只要 config.json 里写了端口），不需要再改管理器配置。

  示例：
    .\New-PigeonServer.ps1 -Root "D:\PGame-TSManager" -Name "生存" -Port 7777 -RestPort 7878
    .\New-PigeonServer.ps1 -Root "D:\PGame-TSManager" -Name "生存2" -Port 7778 -RestPort 7879 -Template "1.生存"

  参数：
    -Root               PGame-TSManager 根目录（默认 D:\59934\Desktop\PGame-TSManager）
    -Name               服务器名（不要带序号，脚本自己加）
    -World              世界文件名（相对 Servers\Worlds\，可不带 .wld；不填用 <名字>.wld）
    -Port               游戏端口（必填）
    -RestPort           REST 端口（必填）
    -Template           模板服目录名（复制它的 tshock\ 配置；不填用第一台）
    -PluginsFromTemplate 插件清单也照抄模板服的
    -Force              目标目录已存在时覆盖 config.json
#>
param(
    [string]$Root = "D:\59934\Desktop\PGame-TSManager",
    [Parameter(Mandatory=$true)][string]$Name,
    [string]$World,
    [Parameter(Mandatory=$true)][int]$Port,
    [Parameter(Mandatory=$true)][int]$RestPort,
    [string]$Template,
    [switch]$PluginsFromTemplate,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

$serversDir = Join-Path $Root 'Servers\Profiles'
if (-not (Test-Path -LiteralPath $serversDir)) { throw "找不到服务器目录：$serversDir" }

function Get-Ordinal([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return 0 }
    $m = [regex]::Match($text.Trim(), '^(\d+)')
    if (-not $m.Success) { return 0 }
    return [int]$m.Groups[1].Value
}
function Strip-Ordinal([string]$text) {
    $m = [regex]::Match($text.Trim(), '^\d+[\.\u3001\-_ ]*(.+)$')
    if ($m.Success) { return $m.Groups[1].Value }
    return $text.Trim()
}
function Ensure-LocalhostWhitelist([string]$path) {
    $defaultText = "# Localhost`r`n127.0.0.1`r`n`r`n# Uncomment to allow IPs within private ranges`r`n# 10.0.0.0/8`r`n# 172.16.0.0/12`r`n# 192.168.0.0/16`r`n"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        [IO.File]::WriteAllText($path, $defaultText, $utf8)
        return
    }
    $lines = [IO.File]::ReadAllLines($path)
    if ($lines -contains '127.0.0.1') { return }
    $text = [IO.File]::ReadAllText($path)
    if (-not $text.EndsWith("`n") -and -not $text.EndsWith("`r")) { $text += "`r`n" }
    $text += "127.0.0.1`r`n"
    [IO.File]::WriteAllText($path, $text, $utf8)
}

$name = Strip-Ordinal $Name
$dirs = Get-ChildItem -LiteralPath $serversDir -Directory | Sort-Object Name
$next = 1
if ($dirs.Count -gt 0) {
    $max = ($dirs | ForEach-Object { Get-Ordinal $_.Name } | Measure-Object -Maximum).Maximum
    if ($max -ge 1) { $next = [int]$max + 1 }
}
$dirName = "$next.$name"
$target  = Join-Path $serversDir $dirName

if ((Test-Path -LiteralPath $target) -and (-not $Force)) { throw "目标已存在：$target（要覆盖 config.json 加 -Force）" }

# 模板服（复制它的 tshock\ 配置与插件清单）
$templateProfile = $null
if ($Template) {
    $templateProfile = $dirs | Where-Object { $_.Name -eq $Template -or $_.Name -like "*$Template" } | Select-Object -First 1
    if (-not $templateProfile) { throw "找不到模板服：$Template" }
} else {
    $templateProfile = $dirs | Select-Object -First 1
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$worldName = if ([string]::IsNullOrWhiteSpace($World)) { "$name.wld" } else { $World }

$plugins = @()
if ($templateProfile) {
    $tplManifest = Join-Path $templateProfile.FullName 'config.json'
    if (Test-Path -LiteralPath $tplManifest) {
        $tpl = [System.IO.File]::ReadAllText($tplManifest) | ConvertFrom-Json
        if ($PluginsFromTemplate -and $tpl.插件) { $plugins = @($tpl.插件) }
        if (-not $World) {
            # 没指定世界就沿用模板的世界名（下面还会提示自行确认）
        }
    }

    $tplTshock = Join-Path $templateProfile.FullName 'tshock'
    $dstTshock = Join-Path $target 'tshock'
    if (Test-Path -LiteralPath $tplTshock) {
        Copy-Item -LiteralPath $tplTshock -Destination $dstTshock -Recurse -Force
        Write-Host "[模板] 已从 $($templateProfile.Name) 复制 tshock\ 配置（端口/数据库/REST 见下一步提示）"
    }
}

$createdTshock = Join-Path $target 'tshock'
[IO.Directory]::CreateDirectory($createdTshock) | Out-Null
Ensure-LocalhostWhitelist (Join-Path $createdTshock 'whitelist.txt')

# 写 TSM 清单 config.json
$manifest = [ordered]@{
    '服务器名称' = $name
    '启用'       = $true
    '世界'       = $worldName
    '语言'       = 7
    '端口'       = $Port
    'REST端口'   = $RestPort
    '最大玩家'   = 252
    'IP'         = '0.0.0.0'
    '密码'       = ''
    '启动参数'   = ''
    '插件'       = $plugins
    '备注'       = "端口 $Port / REST $RestPort"
}
$json = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText((Join-Path $target 'config.json'), $json, $utf8)

Write-Host ''
Write-Host "已创建：$target"
Write-Host "  序号    : $next"
Write-Host "  世界    : $worldName  （把 .wld 放到 Servers\Worlds\ 下）"
Write-Host "  端口    : $Port     REST: $RestPort"
Write-Host "  插件清单: $($plugins.Count) 个"
Write-Host ''
Write-Host '接下来还要做（脚本不代改，避免误伤配置）：'
Write-Host "  1) 打开 $target\tshock\config.json，把："
Write-Host "     - 'Rest的端口'     改成 $RestPort"
Write-Host "     - 'Mysql的数据库名称' / 连接信息 按这台服要用的库改"
Write-Host "     - 'Rest外部应用令牌字典' 里令牌的 用户名/用户组 保持 superadmin"
Write-Host "  2) 把世界文件放到 $Root\Servers\Worlds\"
Write-Host '  3) 重启 PGame-TSManager —— 启动时会自动发现这个目录（不用改管理器 config.json）'
