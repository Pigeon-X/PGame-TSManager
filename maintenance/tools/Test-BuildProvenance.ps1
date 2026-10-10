<#
.SYNOPSIS
    构建产物「同源核验」：打印 SHA256 / 体积，并核对产物内证据（元数据符号名 / 日志字符串）。

.DESCRIPTION
    ⚠ SHA256 不是「同一份源码」的判据，本次有两个反例：
      · 同一份源码 + 同一路径连续两次构建 → 哈希不同（实测 FixTools 5A73752C… vs 12840718…）；
      · 构建失败不会覆盖旧产物 → 会出现「构建失败但哈希未变」的假一致。

    判同源请用这两条：
      ① git log -1（本地）== git log -1（origin）；
      ② 产物内证据 —— 只在该版本里才有的字段名/字符串（本脚本）。

.PARAMETER Path
    待核验的 DLL 路径，可给多个。

.PARAMETER Expect
    期望出现的产物内证据，例如
      'ascii:_threadStopRequested'                  # .NET 元数据串（字段/类型/方法名）
      'utf16:计划书线程未在 2 秒内退出'                # #US 堆里的字面量（UTF-16LE）
    不写前缀时按 ascii 处理。任一项缺失 → 退出码 1。

.EXAMPLE
    ./Test-BuildProvenance.ps1 -Path ..\..\TShock插件维护\publish\BuildOutput\FixTools.dll `
        -Expect ascii:_threadStopRequested,ascii:UseProxy,'utf16:计划书线程未在 2 秒内退出'

.NOTES
    · ascii 串来自元数据 #Strings 堆（字段名/方法名），改名/删除即失效，适合当版本指纹。
    · utf16 串可能落在文件奇数偏移，脚本对 0/1 两种对齐各匹配一次。
    · 本地 PowerShell 5.1 运行本脚本需 UTF-8 BOM（仓库内已带）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path,

    [string[]]$Expect = @()
)

$missing = 0

foreach ($item in $Path)
{
    if (-not (Test-Path -LiteralPath $item))
    {
        Write-Host ("!! 找不到文件: {0}" -f $item)
        $missing++
        continue
    }

    $full = (Resolve-Path -LiteralPath $item).Path
    $info = Get-Item -LiteralPath $full
    $bytes = [System.IO.File]::ReadAllBytes($full)

    Write-Host ("文件   : {0}" -f $full)
    Write-Host ("大小   : {0} B" -f $info.Length)
    Write-Host ("SHA256 : {0}" -f (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash)
    Write-Host ("时间   : {0}" -f $info.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))

    if ($Expect.Count -gt 0)
    {
        # ascii：按 Latin-1 单字节解码，等价于在原始字节流里找 ASCII 串。
        $latin = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
        $utf16Even = [System.Text.Encoding]::Unicode.GetString($bytes)
        $utf16Odd = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)

        foreach ($token in $Expect)
        {
            $kind = 'ascii'
            $needle = $token
            if ($token -match '^(?i)(ascii|utf16)\s*:\s*(.*)$')
            {
                $kind = $Matches[1].ToLowerInvariant()
                $needle = $Matches[2]
            }

            $hits = 0
            if ($kind -eq 'utf16')
            {
                $hits = ([regex]::Matches($utf16Even, [regex]::Escape($needle))).Count +
                        ([regex]::Matches($utf16Odd, [regex]::Escape($needle))).Count
            }
            else
            {
                $hits = ([regex]::Matches($latin, [regex]::Escape($needle))).Count
            }

            if ($hits -gt 0)
            {
                Write-Host ("  [OK  ] {0,-6} {1}   x{2}" -f $kind, $needle, $hits)
            }
            else
            {
                Write-Host ("  [MISS] {0,-6} {1}" -f $kind, $needle)
                $missing++
            }
        }
    }

    Write-Host ""
}

if ($missing -gt 0)
{
    Write-Warning ("同源核验未通过：{0} 项（文件缺失或缺少产物内证据）" -f $missing)
    exit 1
}

Write-Host "同源核验通过：产物内证据齐全。（另请自行确认 git log -1 本地 == origin）"
