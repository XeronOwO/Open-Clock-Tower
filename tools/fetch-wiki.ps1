#Requires -Version 7.0
<#
.SYNOPSIS
    Fetch the Clocktower Wiki pages this project cites as rule sources, and rebuild the
    local snapshot index.

.DESCRIPTION
    Downloads an explicitly enumerated page list through the MediaWiki API, stores the raw
    wikitext under references/wiki/ (gitignored - the wiki text itself is never committed)
    and writes references/wiki-index.json with page name, fetch date, byte count and SHA256.

    The page list is explicit by design: crawling the whole site is forbidden
    (docs/standard/sources.md section 4). Extend the list by hand when the project starts
    citing a new page.

    The script is idempotent: running it twice on the same day produces byte-identical
    snapshots and an identical index, unless the wiki content itself changed.

    Any failed page aborts the run without writing an index, so a partial snapshot can
    never be mistaken for a complete one.

.PARAMETER BaseUrl
    Wiki base URL. Defaults to the project's authoritative public source.

.PARAMETER OutputDirectory
    Directory for raw wikitext snapshots. Defaults to <repository>/references/wiki.

.PARAMETER IndexPath
    Path of the generated index file. Defaults to <repository>/references/wiki-index.json.

.EXAMPLE
    pwsh -File tools/fetch-wiki.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://clocktower-wiki.gstonegames.com',
    [string]$OutputDirectory,
    [string]$IndexPath
)

$ErrorActionPreference = 'Stop'

$UserAgent = 'OpenClockTower-wiki-fetch/1.0 (local development snapshot)'
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$Today = Get-Date -Format 'yyyy-MM-dd'

# Resolve default paths relative to this script (tools/) so it runs from any working directory.
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '../references/wiki'
}
if ([string]::IsNullOrWhiteSpace($IndexPath)) {
    $IndexPath = Join-Path $PSScriptRoot '../references/wiki-index.json'
}

# ---------------------------------------------------------------------------
# Explicit page list: script pages, character pages, character-type pages, and
# the rule / mechanic pages this project cites or keeps as evidence.
# ---------------------------------------------------------------------------
$Pages = @(
    # --- Script pages ---
    '梦殒春宵'
    '暗流涌动'

    # --- Sects & Violets character pages (25, first-release scope) ---
    # Townsfolk (13)
    '钟表匠'
    '筑梦师'
    '舞蛇人'
    '数学家'
    '卖花女孩'
    '城镇公告员'
    '神谕者'
    '博学者'
    '女裁缝'
    '哲学家'
    '艺术家'
    '杂耍艺人'
    '贤者'
    # Outsiders (4)
    '畸形秀演员'
    '心上人'
    '理发师'
    '呆瓜'
    # Minions (4)
    '镜像双子'
    '女巫'
    '洗脑师'
    '麻脸巫婆'
    # Demons (4)
    '方古'
    '亡骨魔'
    '诺-达鲺'
    '涡流'
    # --- 旅行者（5 名，D-0022 / R-0007：首版范围含剧本自带的旅行者）---
    # 咖啡师此前单独补抓过（2026-10-04 的索引里有记录）却漏在本表；补进来，
    # 免得整表重抓时把它的快照从索引里挤掉（页表与索引必须同源）。
    '怪咖'
    '集骨者'
    '咖啡师'
    '流莺'
    '屠夫'
    # --- Correction-evidence pages ---
    # Matched the old 431-page name check but are NOT Sects & Violets roles:
    # Spirit of Ivory is a Fabled, Zombuul is a Bad Moon Rising demon.
    '圣洁之魂'
    '僵怖'

    # --- Reference character pages outside the first-release script ---
    '占卜师'
    '酒鬼'
    '小恶魔'
    '僧侣'

    # --- Character type pages ---
    '镇民'
    '外来者'
    '爪牙'
    '恶魔'
    '旅行者'
    '传奇角色'

    # --- Setup-adjustment and distribution evidence pages (setup-randomizer) ---
    # 设置调整：`[...]` 设置调整的官方口径（先加总、再按剧本池钳制、默认由镇民补偿）；
    # 男爵 / 无名旅客 / 黯月初升 / 洗衣妇 / 异教领袖 / 军团 / 戏法师：分布表逐行的出处
    # （R-0041：7 / 8 / 11 / 12 / 14 / 15 行有据，5 / 9 / 10 / 13 行由百科例证一步推算）。
    '设置调整'
    '男爵'
    '无名旅客'
    '黯月初升'
    '洗衣妇'
    '异教领袖'
    '军团'
    '戏法师'

    # --- Rule, mechanic and glossary pages (evidence tiers 1-2) ---
    '夜晚行动顺序一览'
    '角色能力类别总览'
    '死亡触发能力'
    '钟楼谜团隐性规则汇总'
    '疯狂'
    '“规则”与“理念”——关于角色能力互动的说明'
    '规则概要'
    '设计师总结的国内玩家对染的错误理解'
    '认知覆盖'
    '获取信息'
    '中毒'
    '相克规则'
    '哪些是“可以但不建议”'
    '重要细节'
    '特殊胜利失败条件'
    '规则解释'
    '暴露角色'
    '要赢了吗？'
    '死后能力保留'
    '进场能力'
    '术语汇总'
    '提名'
    '额外死亡'
    '醉酒'
    '投票'
    '处决'
    '阵营转变'
    '获得能力'
    '持续检测型能力'
    '更换选择目标'
    '复活'
    '回溯型能力'
    '影响'
    '互动干扰'
    '免死'
    '疯狂规则如何运作？——疯狂的小精灵'
    '公开触发能力'
    '限次能力'
    '能力效果干扰'
    '保护'
)

function Get-WikiPageContent {
    <#
    .SYNOPSIS
        Return the raw wikitext of a single wiki page, or throw.
    #>
    param(
        [Parameter(Mandatory = $true)] [string]$Page,
        [Parameter(Mandatory = $true)] [string]$BaseUrl,
        [Parameter(Mandatory = $true)] [string]$UserAgent
    )

    $encodedTitle = [uri]::EscapeDataString($Page)
    $uri = "$BaseUrl/api.php?action=query&prop=revisions&rvslots=main&rvprop=content&format=json&formatversion=2&titles=$encodedTitle"

    $response = Invoke-RestMethod -Uri $uri -Method Get -UserAgent $UserAgent -TimeoutSec 60

    if ($null -ne $response.PSObject.Properties['error']) {
        throw "MediaWiki API error for '$Page': $($response.error.code) - $($response.error.info)"
    }

    $pageObject = @($response.query.pages)[0]
    if ($null -eq $pageObject) {
        throw "MediaWiki API returned no page object for '$Page'."
    }
    if ($null -ne $pageObject.PSObject.Properties['missing']) {
        throw "Page '$Page' does not exist on the wiki."
    }

    $content = $pageObject.revisions[0].slots.main.content
    if ([string]::IsNullOrEmpty($content)) {
        throw "Page '$Page' has no main-slot wikitext."
    }

    return $content
}

# Validate page names early: each page name becomes a file name on disk.
foreach ($page in $Pages) {
    if ($page -match '[\\/:*?"<>|]') {
        throw "Page name contains characters that are invalid in a file name: '$page'"
    }
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$indexFullPath = [System.IO.Path]::GetFullPath($IndexPath)
$indexDirectory = [System.IO.Path]::GetDirectoryName($indexFullPath)

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $indexDirectory -Force | Out-Null

Write-Host "fetch-wiki: pages=$($Pages.Count) source=$BaseUrl"
Write-Host "fetch-wiki: snapshot -> $outputRoot"
Write-Host "fetch-wiki: index    -> $indexFullPath"

# Phase 1 - fetch every page into memory first. Nothing is written to disk until all
# pages succeeded, so a failure or an interruption never leaves a half-updated snapshot
# sitting next to an index that still describes the previous run.
$staged = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
$total = $Pages.Count
$number = 0

foreach ($page in $Pages) {
    $number++
    try {
        $content = Get-WikiPageContent -Page $page -BaseUrl $BaseUrl -UserAgent $UserAgent

        if ($content -match '^\s*#(REDIRECT|重定向)') {
            Write-Warning "[$number/$total] '$page' looks like a redirect page; check the snapshot."
        }

        $staged.Add([pscustomobject]@{
                Page    = $page
                Content = $content
            })

        Write-Host ("[{0}/{1}] FETCH OK  {2}  {3} chars" -f $number, $total, $page, $content.Length)
    }
    catch {
        $failures.Add($page)
        Write-Warning ("[{0}/{1}] FETCH FAIL {2}  {3}" -f $number, $total, $page, $_.Exception.Message)
    }
}

if ($failures.Count -gt 0) {
    Write-Host "fetch-wiki: failed pages: $($failures -join ', ')"
    Write-Host 'fetch-wiki: nothing written - a partial snapshot must never look complete.'
    exit 1
}

# Phase 2 - all pages fetched: write the snapshots and build the index.
$records = [System.Collections.Generic.List[object]]::new()
$totalBytes = 0

foreach ($item in $staged) {
    $fileName = $item.Page + '.wiki'
    $filePath = Join-Path $outputRoot $fileName
    [System.IO.File]::WriteAllText($filePath, $item.Content, $Utf8NoBom)

    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    $sha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $totalBytes += $bytes.Length

    $records.Add([ordered]@{
            page      = $item.Page
            file      = $fileName
            fetchedAt = $Today
            bytes     = $bytes.Length
            sha256    = $sha256
        })

    Write-Host ("WRITE {0}  {1} bytes  sha256={2}" -f $item.Page, $bytes.Length, $sha256.Substring(0, 12))
}

$index = [ordered]@{
    source      = $BaseUrl
    generatedAt = $Today
    pageCount   = $records.Count
    pages       = $records
}

$indexJson = $index | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($indexFullPath, $indexJson + "`n", $Utf8NoBom)

Write-Host ("fetch-wiki: DONE pages={0} totalBytes={1}" -f $records.Count, $totalBytes)
