<#
.SYNOPSIS
核对《梦殒春宵》夜晚顺序表与百科快照是否逐条一致。

.DESCRIPTION
本脚本回答一个问题：**src/OpenClockTower.Rules/NightOrderTable.cs 里的四套序列，
与 references/wiki/ 的两页快照是否逐条一致。**

为什么需要它：顺序表的单元测试（tests/OpenClockTower.Rules.Tests）把期望值硬编码在 C# 里，
与实现是"同一份数据的手抄副本"——两处同源同错时测试仍会全绿。本脚本从**一手来源**
（百科快照 + terminology.md 的角色映射）重新派生序列再比对，构成独立证据。

注意：`references/wiki/` 是 gitignored 的本地快照（百科正文不进仓库），
所以这条核对**无法做成仓库内的门禁**，只能作为本机取证脚本。
快照缺失时脚本直接失败，不静默通过——先运行 tools/fetch-wiki.ps1。

.EXAMPLE
pwsh -File tools/check-night-order.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$terminologyPath = Join-Path $root 'docs/standard/terminology.md'
$orderPath = Join-Path $root 'references/wiki/夜晚行动顺序一览.wiki'
$scriptPath = Join-Path $root 'references/wiki/梦殒春宵.wiki'
$tablePath = Join-Path $root 'src/OpenClockTower.Rules/NightOrderTable.cs'

foreach ($path in @($terminologyPath, $orderPath, $scriptPath, $tablePath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "缺少输入：$path（references/wiki/ 快照请先运行 tools/fetch-wiki.ps1）"
    }
}

# 角色映射取自术语表 §9：中文名 → 英文 slug
$characters = @{}
foreach ($line in Get-Content -LiteralPath $terminologyPath -Encoding UTF8) {
    if ($line -match '^\|\s*([^|`]+?)\s*\|\s*`([a-z0-9\-]+)`\s*\|\s*(镇民|外来者|爪牙|恶魔)\s*\|') {
        $characters[$matches[1].Trim()] = $matches[2]
    }
}

if ($characters.Count -ne 25) {
    throw "术语表 §9 解析出 $($characters.Count) 个角色，预期 25 个——术语表结构变了，请同步本脚本"
}

# 推荐口径：《夜晚行动顺序一览》的「首个夜晚」「其他夜晚」
$special = @{
    '黄昏'                 = 'Dusk'
    '爪牙信息'             = 'MinionInfo'
    '恶魔信息'             = 'DemonInfo'
    '信息类角色行动开始'   = 'InformationActionsBegin'
    '黎明'                 = 'Dawn'
}

$recommended = @{
    '首个夜晚' = [System.Collections.Generic.List[string]]::new()
    '其他夜晚' = [System.Collections.Generic.List[string]]::new()
}

$section = $null
foreach ($line in Get-Content -LiteralPath $orderPath -Encoding UTF8) {
    if ($line -match '^==\s*(首个夜晚|其他夜晚)\s*==\s*$') { $section = $matches[1]; continue }
    elseif ($line -match '^==') { $section = $null; continue }
    if (-not $section) { continue }
    if ($line -match "'''([^']+)'''") {
        $label = $matches[1]
        if ($special.ContainsKey($label)) { $recommended[$section].Add($special[$label]) }
        elseif ($characters.ContainsKey($label)) { $recommended[$section].Add($characters[$label]) }
    }
}

# 原本口径：《梦殒春宵》页「夜晚顺序表」
$fileKinds = @{ 'Dusk' = 'Dusk'; 'Dawn' = 'Dawn'; 'Mi' = 'MinionInfo'; 'Di' = 'DemonInfo' }
$original = @{
    '首个夜晚' = [System.Collections.Generic.List[string]]::new()
    '其他夜晚' = [System.Collections.Generic.List[string]]::new()
}

$section = $null
foreach ($line in Get-Content -LiteralPath $scriptPath -Encoding UTF8) {
    if ($line -match "^\*\s*'''首个夜晚'''") { $section = '首个夜晚'; continue }
    elseif ($line -match "^\*\s*'''其他夜晚'''") { $section = '其他夜晚'; continue }
    elseif ($line -match '^==') { $section = $null; continue }
    if (-not $section) { continue }
    if ($line -match '^\[\[File:([A-Za-z_]+)\.png\|link=([^|]+)\|') {
        $file = $matches[1]
        $link = $matches[2]
        if ($fileKinds.ContainsKey($file)) { $original[$section].Add($fileKinds[$file]) }
        elseif ($characters.ContainsKey($link)) { $original[$section].Add($characters[$link]) }
    }
}

# 实现：NightOrderTable.cs 的四个数组
$arrays = [ordered]@{}
$current = $null
foreach ($line in Get-Content -LiteralPath $tablePath -Encoding UTF8) {
    if ($line -match 'IReadOnlyList<NightOrderEntry>\s+(\w+)\s*=') {
        $current = $matches[1]
        $arrays[$current] = [System.Collections.Generic.List[string]]::new()
        continue
    }

    if (-not $current) { continue }
    if ($line -match 'Action\("([a-z0-9\-]+)"\)') { $arrays[$current].Add($matches[1]); continue }
    if ($line -match 'Step\(NightOrderEntryKind\.(\w+)\)') { $arrays[$current].Add($matches[1]) }
}

$failed = $false

function Compare-Sequence {
    param([string]$Name, $Expected, $Actual)

    $expectedItems = @($Expected)
    $actualItems = @($Actual)
    if (($expectedItems -join '|') -eq ($actualItems -join '|')) {
        Write-Host ("[OK]   {0} ({1} 条)" -f $Name, $expectedItems.Count)
        return $true
    }

    Write-Host ("[DIFF] {0}：快照 {1} 条 / 实现 {2} 条" -f $Name, $expectedItems.Count, $actualItems.Count)
    $max = [Math]::Max($expectedItems.Count, $actualItems.Count)
    for ($index = 0; $index -lt $max; $index++) {
        $expectedValue = if ($index -lt $expectedItems.Count) { $expectedItems[$index] } else { '<缺>' }
        $actualValue = if ($index -lt $actualItems.Count) { $actualItems[$index] } else { '<缺>' }
        if ($expectedValue -ne $actualValue) {
            Write-Host ("       [{0}] 快照={1} 实现={2}" -f $index, $expectedValue, $actualValue)
        }
    }

    return $false
}

Write-Host ("角色映射（术语表 §9）：{0} 个" -f $characters.Count)
$failed = -not (Compare-Sequence 'FirstNight/Original' $original['首个夜晚'] $arrays['FirstNightOriginal'])
$ok = Compare-Sequence 'FirstNight/Recommended' $recommended['首个夜晚'] $arrays['FirstNightRecommended']
$failed = $failed -or (-not $ok)
$ok = Compare-Sequence 'OtherNight/Original' $original['其他夜晚'] $arrays['OtherNightOriginal']
$failed = $failed -or (-not $ok)
$ok = Compare-Sequence 'OtherNight/Recommended' $recommended['其他夜晚'] $arrays['OtherNightRecommended']
$failed = $failed -or (-not $ok)

if ($failed) {
    Write-Host '来源一致性核对失败：顺序表与百科快照不一致（依据 docs/standard/sources.md §5）。'
    exit 1
}

Write-Host '来源一致性核对通过：四套序列与两页快照逐条一致。'
exit 0
