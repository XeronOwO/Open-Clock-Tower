#Requires -Version 7.0
<#
.SYNOPSIS
    抓取印刷规则书的逐字提取文本，写入 references/rulebook/（gitignored）并重建索引。

.DESCRIPTION
    官方印刷规则书原件不在本项目手上（docs/standard/sources.md §1）。本脚本从第三方逐字提取
    文本（boardgame-rules 项目的 extracted 产物）拉取一份快照，供 docs/standard/rulings.md
    引用印刷规则书的条目复核。

    与百科快照同一口径：正文不进仓库，只提交 references/rulebook-index.json 的索引
    （来源 URL / 抓取日期 / 字节数 / SHA256）。索引让"引用所依据的正文有没有被改过"可被复核。

    幂等：同一天重复运行产出逐字节一致的快照与索引；下载失败或内容为空时不写索引。

.PARAMETER SourceUrl
    提取文本的下载地址。默认 boardgame-rules 项目的 blood-on-the-clocktower-rules.txt。

.PARAMETER OutputDirectory
    快照目录。默认 <repository>/references/rulebook。

.PARAMETER IndexPath
    生成的索引文件。默认 <repository>/references/rulebook-index.json。

.EXAMPLE
    pwsh -File tools/fetch-rulebook.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceUrl = 'https://lehi-innovation.github.io/boardgame-rules/extracted/blood-on-the-clocktower-rules.txt',
    [string]$OutputDirectory,
    [string]$IndexPath
)

$ErrorActionPreference = 'Stop'

$UserAgent = 'OpenClockTower-rulebook-fetch/1.0 (local development snapshot)'
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$Today = Get-Date -Format 'yyyy-MM-dd'
$FileName = 'blood-on-the-clocktower-rules.txt'

# 以脚本自身（tools/）为基准解析默认路径，保证任意工作目录下可运行。
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '../references/rulebook'
}
if ([string]::IsNullOrWhiteSpace($IndexPath)) {
    $IndexPath = Join-Path $PSScriptRoot '../references/rulebook-index.json'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$indexFullPath = [System.IO.Path]::GetFullPath($IndexPath)
$indexDirectory = [System.IO.Path]::GetDirectoryName($indexFullPath)

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $indexDirectory -Force | Out-Null

Write-Host "fetch-rulebook: source=$SourceUrl"
Write-Host "fetch-rulebook: snapshot -> $outputRoot"
Write-Host "fetch-rulebook: index    -> $indexFullPath"

# 先完整下载到内存；下载失败或内容为空时一律不落盘，避免半份快照配旧索引。
$response = Invoke-WebRequest -Uri $SourceUrl -Method Get -UserAgent $UserAgent -TimeoutSec 120 -UseBasicParsing

if ($response.StatusCode -ne 200) {
    throw "规则书提取文本下载失败：HTTP $($response.StatusCode)"
}

$content = $response.Content
if ([string]::IsNullOrWhiteSpace($content)) {
    throw '规则书提取文本为空，拒绝写入。'
}

$filePath = Join-Path $outputRoot $FileName
[System.IO.File]::WriteAllText($filePath, $content, $Utf8NoBom)

$bytes = [System.IO.File]::ReadAllBytes($filePath)
$sha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()

$index = [ordered]@{
    source      = $SourceUrl
    sourcePdf   = 'blood-on-the-clocktower-rules.pdf（第三方逐字提取；官方原件待核）'
    generatedAt = $Today
    fetchedAt   = $Today
    file        = "rulebook/$FileName"
    bytes       = $bytes.Length
    sha256      = $sha256
}
$indexJson = $index | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($indexFullPath, $indexJson + "`n", $Utf8NoBom)

Write-Host ("fetch-rulebook: WRITE {0}  {1} bytes  sha256={2}" -f $FileName, $bytes.Length, $sha256.Substring(0, 12))
Write-Host 'fetch-rulebook: DONE'
