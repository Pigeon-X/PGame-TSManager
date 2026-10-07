[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$RunNumber = '',
    [string]$Repository = 'Pigeon-X/PGame-TSManager'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Convert-CodePointsToText([int[]]$CodePoints) {
    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

$usageName = (Convert-CodePointsToText @(20351, 29992, 35828, 26126)) + '.txt'
$updateName = (Convert-CodePointsToText @(26356, 26032, 20869, 23481)) + '.txt'
$releaseUsageName = 'package-usage.zh-CN.txt'
$releaseUpdateName = 'update-notes.txt'
$templatePath = Join-Path $root 'maintenance\templates\package-usage.zh-CN.txt'
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Usage template not found: $templatePath"
}

$usage = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8
$usage = $usage.Replace('{{VERSION}}', $Version)
$usage = $usage.Replace('{{COMMIT}}', $Commit)
$usage = $usage.Replace('{{RUN_NUMBER}}', $RunNumber)
Set-Content -LiteralPath (Join-Path $output $usageName) -Value $usage -Encoding UTF8
Set-Content -LiteralPath (Join-Path $output $releaseUsageName) -Value $usage -Encoding UTF8

$previousTag = ''
& git -C $root describe --tags --abbrev=0 --match 'v*-build.*' HEAD^ 2>$null | ForEach-Object { $previousTag = $_.Trim() }
$logArgs = if (-not [string]::IsNullOrWhiteSpace($previousTag)) {
    @("$previousTag..HEAD")
} else {
    @('-30')
}
$changes = @(& git -C $root log @logArgs --date=short --pretty=format:'- %h %ad %s' 2>$null)
if ($changes.Count -eq 0) {
    $changes = @('- 当前构建未检测到可列出的提交记录。')
}

$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
$updateLines = @(
    'PGame-TSManager 更新内容'
    '========================'
    ''
    "版本：$Version"
    "提交：$Commit"
    "构建号：$RunNumber"
    "生成时间：$generatedAt"
    ''
    '更新内容：'
    $changes
    ''
    '说明：本文件随公用包自动生成，列出上一构建发布之后的主要提交。'
)
Set-Content -LiteralPath (Join-Path $output $updateName) -Value ($updateLines -join "`r`n") -Encoding UTF8
Set-Content -LiteralPath (Join-Path $output $releaseUpdateName) -Value ($updateLines -join "`r`n") -Encoding UTF8

$releaseBody = @(
    "# PGame-TSManager $Version build $RunNumber"
    ''
    ('- 仓库：https://github.com/{0}' -f $Repository)
    ('- 提交：`{0}`' -f $Commit)
    ('- 构建时间：{0}' -f $generatedAt)
    ''
    '## 下载'
    ''
    '| 文件 | 用途 |'
    '| --- | --- |'
    '| `PGame-TSManager-universal.zip` | 通用管理器与维护工具 |'
    '| `PGame-TSManager-template.zip` | 自包含管理器与空配置模板 |'
    '| `PGame-TSManager-source.zip` | 源码与维护脚本 |'
    ''
    ('每个 ZIP 内附带 `{0}` 和 `{1}`。' -f $usageName, $updateName)
    ('Release 附件使用兼容文件名：`{0}` 和 `{1}`。' -f $releaseUsageName, $releaseUpdateName)
    ''
    '## 更新内容'
    ''
    $changes
    ''
    '## 构建验证'
    ''
    '- GitHub Actions 已完成构建和仓库布局检查。'
    '- 发布附件由自动构建直接生成。'
) -join "`r`n"
Set-Content -LiteralPath (Join-Path $output 'release-body.md') -Value $releaseBody -Encoding UTF8

Write-Host "Package extras created: $output" -ForegroundColor Green
$global:LASTEXITCODE = 0
