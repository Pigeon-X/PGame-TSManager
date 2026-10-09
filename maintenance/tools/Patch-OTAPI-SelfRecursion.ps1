<#
  修补 OTAPI 的「自递归改指入口」缺陷。

  背景：OTAPI/ModFramework 改写 Terraria 时，把原方法改名为 mfwh_<Name>，并生成入口 <Name>（派发 hook 事件后调用 mfwh_<Name>）。
  但对「原方法内部调用自己」的调用点，改写器仍指向入口 <Name>（而不是 mfwh_<Name>），于是每层递归：
        入口帧 + 原方法帧（2× 栈），且每层重复派发一次 hook 事件。
  已确认影响 OTAPI.dll 里约 100 个方法（WorldGen.nextCount / countTiles、Main.Update、Item.Prefix …）。
  后果：WorldGen.nextCount（4 路 flood fill）在深连通地形下爆栈（STATUS_STACK_OVERFLOW -1073741571），间歇复现。

  本补丁：把 mfwh_X 方法体内「指向同一类型入口 X 的 call/callvirt」改成「指向 mfwh_X」。
  - 纯 IL 操作数改写，签名一致，语义等价于原版自递归（1 帧、事件只派发一次）
  - 幂等：已打过的会显示 0 处改动
  - 只动 OTAPI.dll；请对「上游原始 DLL」执行，便于随 TShock 上游更新重打

  用法：
    .\Patch-OTAPI-SelfRecursion.ps1 -OtapiPath "D:\...\Core\bin\OTAPI.dll" -CecilPath "D:\...\Core\bin\Mono.Cecil.dll" -WhatIfOnly
    .\Patch-OTAPI-SelfRecursion.ps1 -OtapiPath "..." -CecilPath "..." [-Backup]
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OtapiPath,
    [Parameter(Mandatory = $true)][string]$CecilPath,
    [switch]$Backup,
    [switch]$WhatIfOnly
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $OtapiPath)) { throw "OTAPI.dll 不存在：$OtapiPath" }
if (-not (Test-Path -LiteralPath $CecilPath)) { throw "Mono.Cecil.dll 不存在：$CecilPath" }
if (-not ('Mono.Cecil.AssemblyDefinition' -as [type])) { Add-Type -Path $CecilPath }

$target = (Resolve-Path -LiteralPath $OtapiPath).Path
Write-Host ("OTAPI: " + $target) -ForegroundColor Cyan

# 必须 InMemory 读取：默认 ReadAssembly(path) 会一直占用文件句柄，导致后续 Write 撞锁
$readerParams = New-Object Mono.Cecil.ReaderParameters
$readerParams.InMemory = $true
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($target, $readerParams)
$totalMethods = 0
$totalSites = 0
$details = New-Object System.Collections.Generic.List[string]

function Get-AllTypes([Mono.Cecil.TypeDefinition]$t) {
    $t
    foreach ($n in $t.NestedTypes) { Get-AllTypes $n }
}

foreach ($top in $asm.MainModule.Types) {
    foreach ($type in (Get-AllTypes $top)) {
        if (-not $type.HasMethods) { continue }
        foreach ($m in $type.Methods) {
            if (-not $m.HasBody) { continue }
            if (-not $m.Name.StartsWith('mfwh_')) { continue }
            $entryName = $m.Name.Substring(5)
            if ([string]::IsNullOrWhiteSpace($entryName)) { continue }

            # 同类型里找不到入口方法就跳过（可能入口在别处/被裁剪）
            $entry = $type.Methods | Where-Object {
                $_.Name -eq $entryName -and $_.Parameters.Count -eq $m.Parameters.Count -and
                ($_.Parameters.Count -eq 0 -or ($_.Parameters[0].ParameterType.FullName -eq $m.Parameters[0].ParameterType.FullName))
            } | Select-Object -First 1
            if (-not $entry) { continue }

            $rewritten = 0
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Call -and $ins.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Callvirt) { continue }
                $op = $ins.Operand
                if ($op -eq $null) { continue }
                $declType = $null
                try { $declType = $op.DeclaringType } catch { }
                if ($declType -eq $null) { continue }
                if ($declType.FullName -ne $type.FullName) { continue }
                if ($op.Name -ne $entryName) { continue }
                if ($op.Parameters.Count -ne $m.Parameters.Count) { continue }

                # 同签名 → 改指 mfwh_<entryName>
                $newRef = New-Object Mono.Cecil.MethodReference(
                    $m.Name,
                    [Mono.Cecil.TypeReference]$m.ReturnType,
                    [Mono.Cecil.TypeReference]$type)
                foreach ($p in $m.Parameters) {
                    $newRef.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition(
                        [Mono.Cecil.TypeReference]$p.ParameterType)))
                }
                $newRef.HasThis = $m.HasThis
                $ins.Operand = $newRef
                $rewritten++
            }
            if ($rewritten -gt 0) {
                $totalMethods++
                $totalSites += $rewritten
                $details.Add(($type.FullName + '::' + $m.Name + '  -> ' + $rewritten + ' 处'))
            }
        }
    }
}

Write-Host ("待改写：{0} 个方法 / {1} 处调用点" -f $totalMethods, $totalSites) -ForegroundColor Yellow
$details | Sort-Object | ForEach-Object { Write-Host ('  ' + $_) }

if ($WhatIfOnly) {
    $asm.Dispose()
    Write-Host 'WhatIfOnly：未写盘。' -ForegroundColor Cyan
    exit 0
}

if ($totalSites -eq 0) { $asm.Dispose(); Write-Host '无需改动。' -ForegroundColor Green; exit 0 }

if ($Backup) {
    $bak = $target + '.pre-selfrecursion-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $target -Destination $bak -Force
    Write-Host ('备份：' + $bak) -ForegroundColor DarkGray
}

$asm.Write($target)
$asm.Dispose()
Write-Host ("已写盘：" + $target) -ForegroundColor Green
Write-Host '提示：只对上游原始 DLL 执行；TShock 上游更新 OTAPI 后重跑本脚本即可。' -ForegroundColor DarkGray
