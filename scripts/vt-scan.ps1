<#
.SYNOPSIS
    把指定的 exe 送 VirusTotal，產生偵測結果的 Markdown (供 Release notes / run Summary 使用)，
    並可對照 baseline 白名單，標記「出現白名單以外新命中、建議送 Hybrid Analysis」的檔案。

.DESCRIPTION
    對每個檔案先用 SHA256 查 VirusTotal，若 VT 已有該雜湊就直接讀結果；
    沒有 (404) 就上傳 (>=32MB 會先取大檔上傳網址)，再輪詢分析到完成或逾時。
    最後輸出一段 Markdown：偵測統計表；給了 -Baseline 時另外做 HA gate——
    逐筆檢查 malicious/suspicious 命中，扣掉白名單 (accepted_results 子字串 / accepted_engines)
    後殘餘 > threshold 的檔案就標記並附上送 HA 的指令 (不自動送)。

    注意：
    - 剛上傳的新檔偵測數常會延遲回穩 (ML 引擎對全新檔易誤報)，表格是擷取當下的快照，連結才是即時結果。
    - VirusTotal 免費公開 API 有速率限制 (4 次/分)，檔案多時建議用付費 key 或調大 -PollSec。
    - 上傳會讓檔案對 VT 其他使用者可見；此腳本預期在檔案已發佈成公開 release 後才執行。

.PARAMETER Files
    要掃描的檔案路徑陣列。
.PARAMETER ApiKey
    VirusTotal API key，預設讀環境變數 VT_API_KEY。缺少時只印警告並結束 (不中斷)。
.PARAMETER OutFile
    把 Markdown 另外寫到這個檔 (UTF-8 無 BOM)；不給就只輸出到 stdout。
.PARAMETER Baseline
    vt-baseline.json 路徑。給了才啟用 HA gate (多一欄「白名單外」與送 HA 建議)。
.PARAMETER FlagsOut
    把 gate 結果寫成 json { anyFlagged, flagged:[檔名...] }，供後續步驟判斷。
.PARAMETER PublishHint
    Summary 中 HA 指令裡的本機 publish 路徑提示 (CI 不知道你本機實際路徑)。
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
    [string]$Baseline,
    [string]$FlagsOut,
    [string]$PublishHint = '<你的 publish 路徑>',
    [int]$TimeoutSec = 600,
    [int]$PollSec = 20,
    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'
if (-not $ApiKey) { Write-Warning 'VT: 未提供 API key (VT_API_KEY)，略過掃描。'; return }

$base = 'https://www.virustotal.com/api/v3'
$hdr = @{ 'x-apikey' = $ApiKey }

# --- baseline (HA gate) ---
$useGate = $false
$threshold = 0
$acceptedResults = @()
$acceptedEngines = @()
if ($Baseline) {
    $useGate = $true
    if (Test-Path -LiteralPath $Baseline) {
        try {
            $bl = Get-Content -Raw -Encoding utf8 -LiteralPath $Baseline | ConvertFrom-Json
            if ($null -ne $bl.threshold) { $threshold = [int]$bl.threshold }
            if ($bl.accepted_results) { $acceptedResults = @($bl.accepted_results) }
            if ($bl.accepted_engines) { $acceptedEngines = @($bl.accepted_engines) }
        }
        catch { Write-Warning "VT: 無法讀 baseline $Baseline ($($_.Exception.Message))，視為空白白名單。" }
    }
    else { Write-Warning "VT: 找不到 baseline $Baseline，視為空白白名單。" }
}

function Test-Accepted($engine, $result) {
    foreach ($ae in $acceptedEngines) { if ($ae -and ($engine -ieq [string]$ae)) { return $true } }
    $r = ([string]$result).ToLowerInvariant()
    foreach ($ar in $acceptedResults) {
        $a = ([string]$ar).ToLowerInvariant()
        if ($a -and $r -and $r.Contains($a)) { return $true }
    }
    return $false
}

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
    $residual = @()
    if ($rep) {
        $st = $rep.data.attributes.last_analysis_stats
        $mal = [int]$st.malicious
        $susp = [int]$st.suspicious
        $tot = ($st.PSObject.Properties | Measure-Object -Property Value -Sum).Sum
        $detect = "$($mal + $susp) / $tot"
        if ($note) { $detect = "$detect ($note)" }
        $results = $rep.data.attributes.last_analysis_results
        if ($results) {
            foreach ($p in $results.PSObject.Properties) {
                $cat = [string]$p.Value.category
                if ($cat -eq 'malicious' -or $cat -eq 'suspicious') {
                    if (-not (Test-Accepted $p.Name $p.Value.result)) {
                        $residual += ("{0}={1}" -f $p.Name, $p.Value.result)
                    }
                }
            }
        }
    }
    $flagged = $useGate -and ($null -ne $rep) -and ($residual.Count -gt $threshold)
    $rows += [pscustomobject]@{
        Name     = $name
        Detect   = $detect
        Url      = $gui
        Residual = $residual
        RCount   = $residual.Count
        Flagged  = $flagged
        Label    = [IO.Path]::GetFileNameWithoutExtension($name)
    }
    Start-Sleep -Seconds 1
}

$lines = @()
$lines += '## VirusTotal 掃描結果'
$lines += ''
$lines += ("掃描時間 (UTC)：{0}" -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))
$lines += ''
if ($useGate) {
    $lines += '| 檔案 | 偵測 (mal+susp / 總引擎) | 白名單外 | 報告 |'
    $lines += '| :-- | :-- | :-- | :-- |'
    foreach ($r in $rows) { $lines += "| $($r.Name) | $($r.Detect) | $($r.RCount) | [即時報告]($($r.Url)) |" }
}
else {
    $lines += '| 檔案 | 偵測 (malicious+suspicious / 總引擎) | 報告 |'
    $lines += '| :-- | :-- | :-- |'
    foreach ($r in $rows) { $lines += "| $($r.Name) | $($r.Detect) | [即時報告]($($r.Url)) |" }
}
$lines += ''
$lines += '> 新上傳的檔案偵測數可能延遲回穩 (部分引擎對全新檔易誤報)，表格為擷取當下的快照；點連結看即時結果。'

if ($useGate) {
    $flaggedRows = @($rows | Where-Object { $_.Flagged })
    $lines += ''
    if ($flaggedRows.Count -eq 0) {
        $lines += ("### ✅ 不需送 Hybrid Analysis — 無白名單以外的命中 (threshold={0})" -f $threshold)
    }
    else {
        $lines += '### ⚠️ 建議送 Hybrid Analysis'
        $lines += ''
        $lines += ("下列 exe 出現 baseline 白名單以外的 VT 命中 (threshold={0})，建議本機送 HA 深挖靜態簽章：" -f $threshold)
        foreach ($r in $flaggedRows) {
            $lines += ''
            $lines += ("- **{0}** — 白名單外 {1} 筆：{2}" -f $r.Name, $r.RCount, (($r.Residual) -join '；'))
            $lines += '  ```bash'
            $lines += ("  ~/.ultimatektv/scan.sh /tmp/scan {0} `"{1}/{2}`"" -f $r.Label, $PublishHint, $r.Name)
            $lines += '  ```'
        }
        $lines += ''
        $lines += '> 確認為已知誤判後，把該命中的結果字串加進 `scripts/vt-baseline.json` 的 `accepted_results`，下次就不再提醒。'
    }
}

$md = ($lines -join "`n") + "`n"

Write-Output $md
if ($OutFile) { [IO.File]::WriteAllText($OutFile, $md, (New-Object Text.UTF8Encoding($false))) }

if ($useGate -and $FlagsOut) {
    $flaggedNames = @($rows | Where-Object { $_.Flagged } | ForEach-Object { $_.Name })
    $obj = [pscustomobject]@{ anyFlagged = ($flaggedNames.Count -gt 0); flagged = $flaggedNames }
    ($obj | ConvertTo-Json -Compress) | Set-Content -LiteralPath $FlagsOut -Encoding utf8
}
