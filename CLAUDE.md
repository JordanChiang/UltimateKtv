# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

UltimateKtv：Windows 上的 KTV 點歌/播放系統 (WPF, .NET 8, x86/x64)。主控台點歌、電視畫面 (第二螢幕) 播放、手機網頁遠端點歌、YouTube 搜尋下載。
程式碼註解、文件、UI 字串皆為繁體中文；領域用語以 [GLOSSARY.md](GLOSSARY.md) 為準 (例如「待播清單」不叫 playlist/queue、「播放場次」不叫 player)，新程式碼與註解請沿用。

## 建置與測試

```bash
# 一定要 build 整個方案：Updater / StyleSimulator / Tests 以「連結原始檔」方式編譯主程式的部分檔案
dotnet build UltimateKtv.sln -p:Platform=x64 -nologo

# 測試 (xUnit)
dotnet test Tests/UltimateKtv.Tests/UltimateKtv.Tests.csproj -p:Platform=x64

# 單一測試
dotnet test Tests/UltimateKtv.Tests/UltimateKtv.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~SongQueryTests.Sort"
```

- 平台只有 x86 / x64，沒有 Any CPU。x64 輸出 `UltimateKtv.exe` 並使用 `Codec\`；x86 輸出 `UltimateKtv_x86.exe` 並使用 `Codec_x86\`。`dotnet sln add` 會加入 Any CPU 設定，需手動改成只有 x86/x64。
- `libmpv-2.dll` 超過 GitHub 檔案上限不在 repo 中；build 前的 `FetchLibmpv` target 會呼叫 `Tools/Get-Libmpv.ps1` 下載。x86 必須用 20260412 版 (之後的 i686 版初始化即 abort)。
- PostBuild 會把輸出目錄的 `*.dll` 搬到 `libs\`，並複製 Codec、Images 與 Updater exe；輸出根目錄看不到相依 DLL 是正常的。
- 主程式執行中會鎖住 bin 內的 exe；驗證 build 時可加 `-p:OutDir=<其他資料夾>/`。
- XAML 的 `StaticResource` 缺漏、資源型別錯誤只會在執行時爆 (build 會過)。改了 XAML 資源要實際啟動並查看執行目錄的 `UltimateKtv_Log.txt`。
- 未啟用分析器 (`RunAnalyzersDuringBuild=False`)，沒有 lint 步驟。

### 共用 (連結) 原始檔的限制

這些檔案被其他專案以 `<Compile Include="..\X.cs" Link=...>` 編譯，**不可引用只存在主程式的類別** (例如 `Loc`、WPF 視窗、`UKtvPlayer`)：

| 專案 | 連結的檔案 |
| :--- | :--- |
| `Updater` (net471, 一律 x86) | `Services/ZipHelper.cs` |
| `StyleSimulator` | `AppSettings.cs`, `ThemeSettings.cs`, `RowStyleBrushes.cs` |
| `Tests/UltimateKtv.Tests` | `Playback/*.cs`, `AppLogger.cs`, `Db.cs`, `DbHelper.cs`, `DataTableExtension.cs`, `SongLibrary.cs`, `Song.cs`, `SongQuery.cs`, `Youtube/YoutubeVideoId.cs`, `Youtube/YoutubeDownloadQueue.cs`, `AppSettings.cs`, `ThemeSettings.cs`, `SettingsJson.cs` |

因此 `Playback/` 底下的檔案必須維持不依賴 WPF / 播放引擎；與 player 的 adapter (`PlayerTarget`) 放在 `MainWindow.Player.cs`。主程式 csproj 用 `DefaultItemExcludes` 排除 `Updater\**`、`StyleSimulator\**`、`Tests\**`。

## 架構

### 主視窗 = 一個 partial class 拆成多檔
`MainWindow` 依功能拆成 `MainWindow.*.cs` (Player、WaitingList、QuickSearch、Favorites、Youtube、RandomPlay、Pitch、Filters、Views、Display、Settings…)，共用同一份 `MainWindow.xaml`。大部分 UI 邏輯在這裡，找功能時先看檔名對應的 partial。

### 播放：三層
1. **`Playback/PlaybackSession`** — 擁有待播清單與正在播放的歌，決定何時開播下一首 (點歌、插播、切歌、重唱、單曲循環、隨機歌被切掉)。只能在 UI 執行緒呼叫；待播清單只能透過它的方法修改。只依賴 `IPlaybackTarget` (Open/Stop + Opened/Ended/Failed)，所以可用 `FakePlaybackTarget` 單元測試。
   開播後要做的紀錄在 **`Playback/PlayRecorder`** (第一首清空今日點唱、記錄點唱、播放進度過門檻加播放次數、YouTube 長度，隨機歌不記)；錄音的開始/停止也是訂閱場次事件 (`MainWindow.Player.cs`)。
2. **`MainWindow.Player.cs` 的 `PlayerTarget`** — `IPlaybackTarget` 的實作，開檔前套用音量/聲道設定，並把引擎事件帶回 UI 執行緒。
3. **`Player/UKtvPlayer`** — 播放引擎介面，兩個實作：`DirectShowPlayer` (預設，CrazyKTV_MediaKit + LAV filters) 與 `MpvPlayer` (libmpv，`PlayerEngine=1`)。`MediaHostManager` 是全程式唯一 player 的 singleton，libmpv 載入失敗時退回 DirectShow。

libmpv 的影片是原生視窗，WPF 元素疊不上去：電視畫面的跑馬燈/歌詞由 `ITvOverlay` 處理 (`Player/MpvOsd.cs` 用 mpv overlay 合成；DirectShow 直接用 WPF 疊加)，主控台預覽是第二個 mpv 軟體渲染實例 (`MpvPreview` → `D3DImageSurface`)。兩個引擎的音訊處理 (消人聲、增益、限制器、音量) 必須與 MediaKit 一致。

### 歌庫 / 資料庫
- **`Db`** — 唯一的資料庫入口。執行目錄有 `CrazySong.db` 就用 SQLite，否則用 `CrazySong.mdb` (Access：x64 用 ACE.OLEDB.12.0、x86 用 Jet 4.0)。SQL 一律用 `?` 位置參數；兩種資料庫的 SQL 差異 (例如 `IIF` vs `COALESCE`) 用 `Db.IsAccess` 分支。決定資料庫種類的是靜態建構子，測試必須在第一次碰 `Db` 前建立 SQLite 檔。
- **`SongDatas`** — 啟動時從 DB 載入的記憶體快取 (歌曲、歌星、語系、年代…)，查詢都打記憶體而不是 DB。
- **`SongQuery`** — 新歌、排行、語系篩選、快速搜尋、歌星的歌與歌曲排序的規則；主畫面、隨機播放、手機點歌共用。外部條件由 `SongQueryOptions` 傳入 (不自己讀設定或系統時間)，所以可純測試。
- **`SongLibrary`** — 歌庫的寫入 (播放次數、音量、伴唱音軌、我的最愛、點唱紀錄、YouTube 紀錄)，SQL 都集中在這裡。寫入後需要時由呼叫端自行重新載入 `SongDatas`。
- `CrazySong.mdb` 是進版控的歌庫檔；播放/切歌會把音量等寫回它，實測腳本若會播放歌曲應先備份。

### 手機遠端點歌 (HTTP)
`HttpServer` (Kestrel，預設 port 8080，被占用時自動換 port) 服務 `wwwroot/index.html` 單頁，以 query type 字串分派 API (`searchsong`、`ordersong`、`orderyoutube`、`skipsong`、`voldown`、`quit`…，見 `HttpServer.cs` 中的 `queryType switch`)，實作在 `HttpServerHelper`，並以 SSE (`events`) 推播狀態。這些 API 也可用來自動化點歌做實機驗證。
HTTP 請求在 Kestrel 執行緒上；需要碰主程式 (點歌、播放控制、待播清單、YouTube 佇列) 的 handler 一律透過 `RemoteControl` (`HttpServer.Remote`)，由它切到 UI 執行緒，不要在 `HttpServerHelper` 直接用 `Application.Current.MainWindow` 或 `Dispatcher`。新增指令時在 `RemoteControl` 加方法：與主畫面按鈕相同、不需結果的動作用 `Post` (BeginInvoke，立即返回)，需要結果的用 `Invoke`。

### 設定、主題、語言
- `settings.json` (執行目錄) ↔ `AppSettings`，由 `SettingsManager` 載入；`SettingsJson.Load` 是容錯載入：單一項目錯誤只退回該項預設值並回寫修正。屬性上的 `[Description]` 會成為設定檔說明。
- 設定的有效範圍以 `AppSettings` 的常數宣告一次，`AppSettings.Normalize()` 在載入與存檔時把超出範圍的值拉回上下限；新增有範圍的設定時加在這裡，不要在各處自己 clamp。設定視窗按「套用」呼叫 `SettingsManager.ApplyChanges(previous)`，需要依設定調整狀態的地方訂閱 `SettingsChanged` (主程式在 `MainWindow.OnSettingsChanged`)。
- `ThemeSettings` / `ThemeSettingsHandler` 管配色與字型；`SampleSettings/theme_*.json` 是範本配色。`Themes/Colors.xaml` 不能用 `<StaticResource x:Key ResourceKey>` 做顏色別名 (會啟動即 XamlParseException)。`StyleSimulator` 是獨立的預覽配色工具。
- `Loc` — 介面多國語言。`Languages/{culture}.json` 為平面 key→字串，載入後寫成 `Str.{key}` 應用程式資源，XAML 用 `{DynamicResource Str.xxx}`、程式用 `Loc.T("key")`。`zh-TW.json` 是基準語言 (也內嵌進 exe)，其他語言缺的 key 退回 zh-TW；新增 UI 字串至少要加到 `zh-TW.json`。`Web.` 開頭的 key 給遠端網頁，依裝置 Accept-Language 回應。

### 其他模組
- `Marquee/` — 跑馬燈 (`MarqueeAPI` → `MarqueeManager` → `MarqueeControl`)，用法見 `Marquee/MARQUEE_USAGE.md`、`STATIC_TEXT_USAGE.md`。
- YouTube — `Youtube/YoutubeDownloadQueue` 是下載佇列 (以影片 ID 去重、一次一部、失敗不中斷、可取消)，下載器在 `IYoutubeDownloader` 後面 (`YtDlpDownloader`：`Tools/yt-dlp.exe` 失敗改用 YoutubeExplode)；`YoutubeVideoId` 是影片 ID 與快取檔名「{歌名}_{videoId}.mp4」的規則。`MainWindow.Youtube.cs` 訂閱佇列事件更新畫面、下載完點歌。點唱紀錄的 SongId 可能是紀錄 Id (`Y0017`) 或影片 ID，比對同一部影片一律用 `YoutubeVideoId.Resolve`。
- `Services/UpdateManager` + `Updater/` — 從 GitHub Release 檢查與套用更新；Updater 是獨立 net471 x86 exe。
- `ResourceMonitor` — 每 5 分鐘與每首歌開播時把記憶體/Handle/GDI 用量寫進 `UltimateKtv_Log.txt`，用來追資源洩漏 (欄位說明見 README)。

## UI 規則

- 需支援觸控螢幕：按鈕與圖示要維持足夠大小。
- 效能優化時保留既有視覺效果 (光暈、陰影、圓角等)，不以拿掉效果換效能。
