<#
.SYNOPSIS
    把指定的 exe 送 VirusTotal，產生偵測結果的 Markdown (供 Release notes 使用)。

.DESCRIPTION
    對每個檔案先用 SHA256 查 VirusTotal，若 VT 已有該雜湊就直接讀結果；
    沒有 (404) 就上傳 (>=32MB 會先取大檔上傳網址)，再輪詢分析到完成或逾時。
    最後輸出一段 Markdown 表格 (檔名、偵測數、即時報告連結)。

    注意：
    - 剛上傳的新檔偵測數常會延遲回穩 (ML 引擎對全新檔易誤報)，表格是擷取當下的快照，
      連結才是即時結果。
    - VirusTotal 免費公開 API 有速率限制 (4 次/分)，檔案多時建議用付費 key 或調大 -PollSec。
    - 上傳會讓檔案對 VT 其他使用者可見；此腳本預期在檔案已發佈成公開 release 後才執行。

.PARAMETER Files
    要掃描的檔案路徑陣列。
.PARAMETER ApiKey
    VirusTotal API key，預設讀環境變數 VT_API_KEY。缺少時只印警告並結束 (不中斷)。
.PARAMETER OutFile
    把 Markdown 另外寫到這個檔 (UTF-8 無 BOM)；不給就只輸出到 stdout。
.PARAMETER TimeoutSec
    單一檔案等待分析完成的上限秒數 (預設 600)。
.PARAMETER PollSec
    輪詢分析狀態的間隔秒數 (預設 20)。
.PARAMETER NoUpload
    只查雜湊、不上傳 (VT 沒有該檔時標「尚未掃描」)。本機驗證用，避免外洩未發佈檔。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Files,
    [string]$ApiKey = $env:VT_API_KEY,
    [string]$OutFile,
    [int]$TimeoutSec = 600,
    [int]$PollSec = 20,
    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'
if (-not $ApiKey) { Write-Warning 'VT: 未提供 API key (VT_API_KEY)，略過掃描。'; return }

$base = 'https://www.virustotal.com/api/v3'
$hdr = @{ 'x-apikey' = $ApiKey }

function Get-VtFileReport($sha) {
    try { return Invoke-RestMethod -Uri "$base/files/$sha" -Headers $hdr -Method Get }
    catch {
        $code = $null
        try { $code = [int]$_.Exception.Response.StatusCode } catch {}
        if ($code -eq 404) { return $null }
        throw
    }
}

function Wait-VtAnalysis($id, [datetime]$deadline) {
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds $PollSec
        try {
            $a = Invoke-RestMethod -Uri "$base/analyses/$id" -Headers $hdr -Method Get
            if ($a.data.attributes.status -eq 'completed') { return $true }
        }
        catch { }
    }
    return $false
}

$rows = @()
foreach ($f in $Files) {
    if (-not (Test-Path -LiteralPath $f)) { Write-Warning "VT: 找不到 $f，略過。"; continue }
    $name = Split-Path $f -Leaf
    $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash.ToLowerInvariant()
    $gui = "https://www.virustotal.com/gui/file/$sha"
    Write-Host "VT: $name ($sha)"

    $rep = Get-VtFileReport $sha
    $note = ''
    if (-not $rep) {
        if ($NoUpload) {
            $note = '尚未掃描'
        }
        else {
            $size = (Get-Item -LiteralPath $f).Length
            $uploadUri = "$base/files"
            if ($size -ge 32MB) { $uploadUri = (Invoke-RestMethod -Uri "$base/files/upload_url" -Headers $hdr -Method Get).data }
            $resp = Invoke-RestMethod -Uri $uploadUri -Headers $hdr -Method Post -Form @{ file = Get-Item -LiteralPath $f }
            $done = Wait-VtAnalysis $resp.data.id ((Get-Date).AddSeconds($TimeoutSec))
            if (-not $done) { $note = '掃描中' }
            $rep = Get-VtFileReport $sha
        }
    }

    $detect = if ($note) { $note } else { 'N/A' }
    if ($rep) {
        $st = $rep.data.attributes.last_analysis_stats
        $mal = [int]$st.malicious
        $susp = [int]$st.suspicious
        $tot = ($st.PSObject.Properties | Measure-Object -Property Value -Sum).Sum
        $detect = "$($mal + $susp) / $tot"
        if ($note) { $detect = "$detect ($note)" }
    }
    $rows += [pscustomobject]@{ Name = $name; Detect = $detect; Url = $gui }
    Start-Sleep -Seconds 1
}

$lines = @()
$lines += '## VirusTotal 掃描結果'
$lines += ''
$lines += ("掃描時間 (UTC)：{0}" -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))
$lines += ''
$lines += '| 檔案 | 偵測 (malicious+suspicious / 總引擎) | 報告 |'
$lines += '| :-- | :-- | :-- |'
foreach ($r in $rows) { $lines += "| $($r.Name) | $($r.Detect) | [即時報告]($($r.Url)) |" }
$lines += ''
$lines += '> 新上傳的檔案偵測數可能延遲回穩 (部分引擎對全新檔易誤報)，表格為擷取當下的快照；點連結看即時結果。'
$md = ($lines -join "`n") + "`n"

Write-Output $md
if ($OutFile) { [IO.File]::WriteAllText($OutFile, $md, (New-Object Text.UTF8Encoding($false))) }
