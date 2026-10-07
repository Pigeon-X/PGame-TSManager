[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$Token,
    [int]$Keep = 10,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if ($Keep -lt 1) { throw 'Keep 必须大于 0。' }
if ([string]::IsNullOrWhiteSpace($Token)) { throw '缺少 GitHub Token。' }

$headers = @{
    'User-Agent' = 'PGame-TSManager'
    'Accept' = 'application/vnd.github+json'
    'Authorization' = 'Bearer ' + $Token
}

$releases = @()
for ($page = 1; $page -le 100; $page++) {
    $batch = @(Invoke-RestMethod `
        -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page" `
        -Headers $headers `
        -TimeoutSec 60)
    if ($batch.Count -eq 0) { break }
    $releases += $batch
    if ($batch.Count -lt 100) { break }
}

$ordered = @($releases | Sort-Object { [datetime]$_.created_at } -Descending)
$remove = @($ordered | Select-Object -Skip $Keep)
Write-Host "Release 总数：$($ordered.Count)，保留：$([Math]::Min($Keep, $ordered.Count))，清理：$($remove.Count)" -ForegroundColor Cyan

foreach ($release in $remove) {
    $tag = [string]$release.tag_name
    if ($DryRun) {
        Write-Host "[预览] 删除 Release：$tag ($($release.id))" -ForegroundColor Yellow
        if ($tag) { Write-Host "[预览] 删除 Tag：$tag" -ForegroundColor DarkYellow }
        continue
    }

    Invoke-RestMethod `
        -Method Delete `
        -Uri "https://api.github.com/repos/$Repository/releases/$($release.id)" `
        -Headers $headers `
        -TimeoutSec 60 | Out-Null
    Write-Host "已删除 Release：$tag" -ForegroundColor DarkGray

    if (-not [string]::IsNullOrWhiteSpace($tag)) {
        try {
            $encodedTag = [Uri]::EscapeDataString($tag)
            Invoke-RestMethod `
                -Method Delete `
                -Uri "https://api.github.com/repos/$Repository/git/refs/tags/$encodedTag" `
                -Headers $headers `
                -TimeoutSec 60 | Out-Null
            Write-Host "已删除 Tag：$tag" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "Tag 已不存在或无需删除：$tag" -ForegroundColor DarkYellow
        }
    }
}

Write-Host 'Release 清理完成。' -ForegroundColor Green
