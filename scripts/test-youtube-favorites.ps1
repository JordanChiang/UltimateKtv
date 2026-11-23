<#
.SYNOPSIS
  YouTube 我的最愛 / 點唱紀錄的實機 HTTP 測試 (不連網)。

.DESCRIPTION
  在指定的執行資料夾 (預設 Release 輸出) 跑一次完整流程，結束後一律還原歌庫與設定檔：
    1. 備份 CrazySong.mdb、settings.json、themesettings.json。
    2. settings.json 改用測試埠、關閉隨機播放、下載資料夾改到暫存資料夾。
    3. 用 ffmpeg 產生 30 秒的無聲測試影片，放進暫存下載資料夾 (檔名「{歌名}_{影片ID}.mp4」)，點歌時直接走快取、不連網。
    4. 在歌庫加入測試使用者與一筆 YouTube 最愛 (YoutubeFavorite 表不存在時照主程式的定義建立)。
    5. 啟動程式，以 HTTP API 驗證：
       - favoritesong：測試使用者的最愛有這部影片 (IsYoutube = true、歌手欄 = YouTube)
       - orderyoutubefavorite：點歌成功且是已下載 (快取)
       - queryplayerstate：開始播放這部影片
       - favoritesong (今日播放 ****、這次開機的點唱紀錄)：開播後都記錄了這部影片 (這次開機的使用者不用重開就列得出來)
       - 再點一次 (正在播的歌可以再點，與歌庫的歌相同) 進入待播清單；第三次點：已在待播清單，不重複
    6. quit 正常結束 (逾時才結束自己啟動的行程)，檢查這次執行的 log 沒有例外，還原備份。

  只支援 Access 歌庫 (執行資料夾有 CrazySong.db 時會改用 SQLite，這裡直接中止)。
  只結束自己啟動的行程；同一資料夾已有程式在執行時中止 (不要碰使用者正在用的程式)。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\test-youtube-favorites.ps1
#>
param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\bin\x64\Release\net8.0-windows'),
    [int]$Port = 18765,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$AppDir = (Resolve-Path $AppDir).Path
$exe = Join-Path $AppDir 'UltimateKtv.exe'
$mdb = Join-Path $AppDir 'CrazySong.mdb'
$logFile = Join-Path $AppDir 'UltimateKtv_Log.txt'
$ffmpeg = Join-Path $AppDir 'Tools\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { $ffmpeg = Join-Path $PSScriptRoot '..\Tools\ffmpeg.exe' }

# 測試資料：影片 ID 只要符合 11 碼格式；使用者 ID 不會與一般使用者重複
$VideoId = 'ytfavtest01'
$VideoName = 'YT最愛測試影片'
$TestUserId = '^YTT'
$TestUserName = 'YT最愛測試'
$TodayUserId = '****'

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Detail = $detail })
    Write-Host ("[{0}] {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

function Get-Api([string]$query) {
    # Invoke-RestMethod 在回應沒有 charset 時會用 ISO-8859-1 解碼，中文會亂碼，所以自己用 UTF-8 解
    $resp = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/?$query" -TimeoutSec 15
    $reader = New-Object System.IO.StreamReader($resp.RawContentStream, [System.Text.Encoding]::UTF8)
    $text = $reader.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function Wait-Until([scriptblock]$condition, [int]$seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try { if (& $condition) { return $true } } catch { }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Invoke-Db([string]$sql, [object[]]$params = @()) {
    $conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$mdb")
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($p in $params) { [void]$cmd.Parameters.AddWithValue('?', $p) }
        return $cmd.ExecuteNonQuery()
    } finally { $conn.Close() }
}

function Get-DbTables {
    $conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$mdb")
    $conn.Open()
    try { return @($conn.GetSchema('Tables').Rows | Where-Object { $_.TABLE_TYPE -eq 'TABLE' } | ForEach-Object { $_.TABLE_NAME }) }
    finally { $conn.Close() }
}

function FavoriteIds([string]$userId) {
    $rows = Get-Api ("queryType=favoritesong&user={0}&page=0&rows=200" -f [Uri]::EscapeDataString($userId))
    return @($rows | Where-Object { $_.IsYoutube -eq $true } | ForEach-Object { $_.Song_Id })
}

# ---------- 前置檢查 ----------
if (-not (Test-Path $exe)) { throw "找不到 $exe，請先 build。" }
if (-not (Test-Path $mdb)) { throw "找不到 $mdb。" }
if (Test-Path (Join-Path $AppDir 'CrazySong.db')) { throw "執行資料夾有 CrazySong.db (SQLite)，這個腳本只支援 Access 歌庫。" }
if (-not (Test-Path $ffmpeg)) { throw "找不到 ffmpeg.exe。" }
$running = Get-Process UltimateKtv* -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($AppDir, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "這個資料夾的程式正在執行 (PID $($running.Id -join ', '))，請先關閉。" }
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listener) { throw "埠 $Port 已被占用 (程式會自動換埠，測試就連不到)，請用 -Port 指定其他埠。" }

$work = Join-Path ([IO.Path]::GetTempPath()) ("ukt-ytfav-" + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $work 'backup'
$downloads = Join-Path $work 'YoutubeDownloads'
New-Item -ItemType Directory -Force $backup, $downloads | Out-Null
$backedUp = @()
$proc = $null
$logLinesBefore = 0

try {
    # ---------- 1. 備份 ----------
    foreach ($f in 'CrazySong.mdb', 'settings.json', 'themesettings.json') {
        $src = Join-Path $AppDir $f
        if (Test-Path $src) { Copy-Item $src $backup -Force; $backedUp += $f }
    }
    Write-Host "已備份 $($backedUp -join ', ') 到 $backup"

    # ---------- 2. 設定檔 (直接換值，不重新序列化整個檔案) ----------
    $settingsPath = Join-Path $AppDir 'settings.json'
    if (Test-Path $settingsPath) {
        $json = [IO.File]::ReadAllText($settingsPath, [Text.Encoding]::UTF8)
        $json = $json -replace '"HttpServerPort"\s*:\s*\d+', ('"HttpServerPort": ' + $Port)
        $json = $json -replace '"RandomPlayEnabled"\s*:\s*(true|false)', '"RandomPlayEnabled": false'
        $escapedDir = $downloads.Replace('\', '\\')
        $json = $json -replace '"YoutubeDownloadDir"\s*:\s*"[^"]*"', ('"YoutubeDownloadDir": "' + $escapedDir.Replace('$', '$$') + '"')
        [IO.File]::WriteAllText($settingsPath, $json, (New-Object Text.UTF8Encoding($false)))
    } else {
        throw "找不到 settings.json (要先啟動過一次程式產生)。"
    }

    # ---------- 3. 測試影片 (快取檔名與主程式 YoutubeVideoId.CacheFileName 相同) ----------
    $videoPath = Join-Path $downloads ("{0}_{1}.mp4" -f $VideoName, $VideoId)
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc=size=640x360:rate=25:duration=30" -f lavfi -i "anullsrc=r=44100:cl=stereo" -t 30 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $videoPath
    if (-not (Test-Path $videoPath)) { throw "ffmpeg 產生測試影片失敗。" }

    # ---------- 4. 歌庫：測試使用者與一筆 YouTube 最愛 ----------
    if ((Get-DbTables) -notcontains 'YoutubeFavorite') {
        # 與 SongLibrary.EnsureYoutubeFavoriteTable 相同的定義
        Invoke-Db "CREATE TABLE YoutubeFavorite (User_Id TEXT(50), VideoId TEXT(16), Name TEXT(255), Length INTEGER, CreatDate DATETIME, CONSTRAINT PK_YoutubeFavorite PRIMARY KEY (User_Id, VideoId))" | Out-Null
    }
    Invoke-Db "INSERT INTO ktv_User (User_Id, User_Name) VALUES (?, ?)" @($TestUserId, $TestUserName) | Out-Null
    Invoke-Db "INSERT INTO YoutubeFavorite (User_Id, VideoId, Name, Length, CreatDate) VALUES (?, ?, ?, ?, ?)" @($TestUserId, $VideoId, $VideoName, 30, (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) | Out-Null

    # ---------- 5. 啟動與驗證 ----------
    if (Test-Path $logFile) { $logLinesBefore = @(Get-Content $logFile -Encoding UTF8).Count }
    $proc = Start-Process $exe -WorkingDirectory $AppDir -PassThru
    Write-Host "已啟動 PID $($proc.Id)，等待 HTTP (埠 $Port)..."
    $up = Wait-Until { $null -ne (Get-Api 'queryType=queryplayerstate') }
    Check 'HTTP 服務啟動' $up
    if (-not $up) { throw "HTTP 沒有回應。" }

    $rows = Get-Api ("queryType=favoritesong&user={0}&page=0&rows=50" -f [Uri]::EscapeDataString($TestUserId))
    $row = @($rows | Where-Object { $_.Song_Id -eq $VideoId }) | Select-Object -First 1
    Check '最愛清單有 YouTube 影片' ($null -ne $row) ("rows=" + @($rows).Count)
    if ($row) {
        Check '最愛列標示 IsYoutube' ($row.IsYoutube -eq $true)
        Check '最愛列歌手欄為 YouTube' ($row.Song_Singer -eq 'YouTube') $row.Song_Singer
        Check '最愛列歌名' ($row.Song_SongName -eq $VideoName) $row.Song_SongName
    }

    $order = Get-Api ("queryType=orderyoutubefavorite&value={0}&user=test" -f $VideoId)
    Check '從最愛點 YouTube 歌成功' ($order.success -eq $true) $order.message
    Check '點歌回報已下載 (走快取，不連網)' ($order.isDownloaded -eq $true)

    $playing = Wait-Until { $s = Get-Api 'queryType=queryplayerstate'; $s.IsPlaying -and $s.SongName -eq $VideoName }
    Check '開始播放這部影片' $playing

    $inToday = Wait-Until { (FavoriteIds $TodayUserId) -contains $VideoId } 20
    Check '今日播放 (****) 記錄了這部影片' $inToday

    # 這次開機的點唱紀錄：使用者 ID 是程式開機時間 "MM/dd HH:mm" (可能與行程啟動時間差一分鐘)，
    # 所以找今天開頭、且清單裡有這部影片的那一位；不用重開就要列得出來
    $today = (Get-Date).ToString('MM/dd', [Globalization.CultureInfo]::InvariantCulture)
    $sessionId = $null
    $inSession = Wait-Until {
        $ids = @((Get-Api 'queryType=favoriteuser') | Where-Object { $_.User_Id -like "$today *" } | ForEach-Object { $_.User_Id })
        foreach ($id in $ids) { if ((FavoriteIds $id) -contains $VideoId) { $script:sessionId = $id; return $true } }
        return $false
    } 20
    Check '這次開機的點唱紀錄 (不用重開) 有這部影片' $inSession ("User_Id=$sessionId")

    # 正在播的歌可以再點一次 (與歌庫的歌相同，重複只比對待播清單)；再點第三次就已在待播清單
    $again = Get-Api ("queryType=orderyoutubefavorite&value={0}&user=test" -f $VideoId)
    Check '播放中的影片可以再點一次' ($again.success -eq $true) $again.message
    $queued = Wait-Until { (Get-Api 'queryType=queryplayerstate').PlaylistCount -ge 1 } 20
    Check '再點的影片進入待播清單' $queued
    $third = Get-Api ("queryType=orderyoutubefavorite&value={0}&user=test" -f $VideoId)
    Check '已在待播清單的影片不重複點歌' ($third.success -ne $true) $third.message

    $missing = Get-Api 'queryType=orderyoutubefavorite&value=notafavorit&user=test'
    Check '不在最愛的影片 ID 回報錯誤' ($missing.success -ne $true)

    # ---------- 6. 結束 ----------
    try { Get-Api 'queryType=quit' | Out-Null } catch { }
    $exited = $proc.WaitForExit(20000)
    Check '程式正常結束 (quit)' $exited
}
catch {
    Check '腳本執行' $false $_.Exception.Message
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Write-Host "結束自己啟動的行程 PID $($proc.Id)"
        Stop-Process -Id $proc.Id -Force
        $proc.WaitForExit(10000) | Out-Null
    }

    if (Test-Path $logFile) {
        $newLines = @(Get-Content $logFile -Encoding UTF8 | Select-Object -Skip $logLinesBefore)
        $errors = @($newLines | Where-Object { $_ -match 'Exception|\[ERROR\]|XamlParse' })
        Check '這次執行的 log 沒有例外' ($errors.Count -eq 0) ($errors | Select-Object -First 3 | Out-String).Trim()
    }

    foreach ($f in $backedUp) { Copy-Item (Join-Path $backup $f) $AppDir -Force }
    Write-Host "已還原 $($backedUp -join ', ')"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

$failed = @($results | Where-Object { $_.Result -eq 'FAIL' })
Write-Host ''
Write-Host ("結果：{0} 項通過，{1} 項失敗" -f ($results.Count - $failed.Count), $failed.Count)
if ($failed.Count -gt 0) { exit 1 }
