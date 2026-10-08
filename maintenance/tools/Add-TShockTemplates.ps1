[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ManagerDir
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ManagerDir).Path
$templatePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'templates\tshock-generic.zh-CN.json'
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "TShock 模板不存在：$templatePath"
}
$whitelistTemplatePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'templates\whitelist.txt'
if (-not (Test-Path -LiteralPath $whitelistTemplatePath -PathType Leaf)) {
    throw "白名单模板不存在：$whitelistTemplatePath"
}

$profilesDir = Join-Path $root 'Servers\Profiles'
$worldsDir = Join-Path $root 'Servers\Worlds'
$pluginsDir = Join-Path $root 'Plugins'
$coreDir = Join-Path $root 'Core'
$toolsDir = Join-Path $root 'Tools'
foreach ($dir in @($profilesDir, $worldsDir, $pluginsDir, $coreDir, $toolsDir)) {
    [IO.Directory]::CreateDirectory($dir) | Out-Null
}

$runtimeDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
$defaultPlugins = @('TShockAPI.dll', 'HotReload.dll')
foreach ($pluginName in $defaultPlugins) {
    $source = Join-Path $runtimeDir $pluginName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "通用模板缺少运行时插件：$source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $pluginsDir $pluginName) -Force
}

function Set-Property([object]$Object, [string]$Name, [object]$Value) {
    if ($null -ne $Object.PSObject.Properties[$Name]) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }
}

$profiles = @()
foreach ($definition in @(
    [pscustomobject]@{ Name = '生存';  Directory = '1.生存';  Port = 7777; RestPort = 7878; World = '生存.wld' },
    [pscustomobject]@{ Name = '生存2'; Directory = '2.生存2'; Port = 7778; RestPort = 7879; World = '生存2.wld' }
)) {
    $profileDir = Join-Path $profilesDir $definition.Directory
    $tshockDir = Join-Path $profileDir 'tshock'
    [IO.Directory]::CreateDirectory($tshockDir) | Out-Null

    $tshock = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Set-Property $tshock.Settings '服务器名称' $definition.Name
    Set-Property $tshock.Settings '服务器端口' $definition.Port
    Set-Property $tshock.Settings 'Rest的端口' $definition.RestPort
    [IO.File]::WriteAllText(
        (Join-Path $tshockDir 'config.json'),
        ($tshock | ConvertTo-Json -Depth 30),
        [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $whitelistTemplatePath -Destination (Join-Path $tshockDir 'whitelist.txt') -Force

    $manifest = [ordered]@{
        '服务器名称' = $definition.Name
        '启用' = $true
        '世界' = $definition.World
        '语言' = 7
        '端口' = $definition.Port
        'REST端口' = $definition.RestPort
        '最大玩家' = 16
        'IP' = '0.0.0.0'
        '密码' = ''
        '启动参数' = ''
        '插件' = @($defaultPlugins)
        '插件总库' = 'Plugins'
        '覆盖插件目录' = $true
        '备注' = "通用模板 $($definition.Port) / REST $($definition.RestPort)"
    }
    [IO.File]::WriteAllText(
        (Join-Path $profileDir 'config.json'),
        ($manifest | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    $profiles += [ordered]@{
        name = $definition.Name
        rootPath = "Servers\Profiles\$($definition.Directory)"
        executable = 'TShock.Server.exe'
        arguments = ''
        enabled = $true
        remark = "通用模板 $($definition.Port) / REST $($definition.RestPort)"
        plugins = @($defaultPlugins)
        pluginLibrary = ''
    }
}

$configPath = Join-Path $root 'config.json'
$config = if (Test-Path -LiteralPath $configPath) {
    Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}

Set-Property $config 'backupBeforeStart' $true
Set-Property $config 'backupDir' 'Core\Backups'
Set-Property $config 'backupKeep' 10
Set-Property $config 'pluginLibrary' 'Plugins'
Set-Property $config 'syncPluginsOnStart' $true
Set-Property $config 'prunePlugins' $true
Set-Property $config 'disabledPluginDir' 'ServerPlugins.disabled'
Set-Property $config 'serverProfiles' @($profiles)
Set-Property $config 'showServerWindow' $false
Set-Property $config 'worldDir' 'Servers\Worlds'
Set-Property $config 'pluginDir' 'Plugins'
Set-Property $config 'runtimeDir' 'Core\_runtime'
Set-Property $config 'serverDir' 'Servers\Profiles'
Set-Property $config 'configFile' 'config.json'
Set-Property $config 'serverExecutable' 'TShock.Server.exe'
Set-Property $config 'sharedRuntimeDir' 'Core'
Set-Property $config 'logDir' 'Core\Logs'
Set-Property $config 'dataDir' 'Core\Data'
Set-Property $config 'toolsDir' 'Tools'
Set-Property $config 'startAllSequential' $true
Set-Property $config 'watchdogEnabled' $true
Set-Property $config 'alertEnabled' $true

[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))

[IO.File]::WriteAllText((Join-Path $worldsDir '.gitkeep'), '', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $pluginsDir '.gitkeep'), '', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $coreDir '.gitkeep'), '', [Text.UTF8Encoding]::new($false))

Write-Host 'TShock 模板已加入：1.生存 (7777/7878)、2.生存2 (7778/7879)' -ForegroundColor Green
