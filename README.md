# UltimateKtv

UltimateKtv 是一套針對 Windows 平台開發的專業 KTV 點歌與播放系統，支援雙螢幕獨立顯示、手機網頁端遠端點歌、YouTube 線上影音搜索播放以及硬體加速解碼等多項進階功能，為家庭娛樂提供完整的解決風格。

## 系統建置環境 (Build Environment)

| 項目 | 說明 |
| :--- | :--- |
| **作業系統 (OS)** | Windows 10 / 11 |
| **開發框架 (Framework)** | .NET 8.0 (`net8.0-windows`) |
| **使用者介面 (UI)** | WPF (Windows Presentation Foundation) |
| **目標平台 (Platform)** | x86 / x64 |
| **開發工具 (IDE)** | Visual Studio 2022 |
| **核心套件 (NuGet)** | <ul><li>`MaterialDesignThemes` (UI 視覺框架)</li><li>`Microsoft.AspNetCore.Server.Kestrel` (內建網頁伺服器)</li><li>`System.Data.OleDb` / `System.Data.SQLite` (資料庫存取)</li><li>`YoutubeExplode` (YouTube 解析支援)</li><li>`QRCoder` (網頁點歌 QR Code 產生)</li></ul> |
| **多媒體元件** | CrazyKTV_MediaKit, DirectShowLib-2005, FFmpeg |

---

## 記錄檔資源使用紀錄 (Resource Usage Log)

程式執行目錄下的 `UltimateKtv_Log.txt` 會記錄程式的資源使用情況，用於追查記憶體洩漏或長時間執行後變慢的問題。

**記錄時機**
- 程式啟動時 (`Startup`)
- 之後每 5 分鐘一筆 (`Periodic`)
- 每首歌曲開始播放時 (`Song started`)
- 程式關閉時 (`Shutdown`)

**格式範例**
```
[21:05:00.123] [INFO] [Resource] Song started: WorkingSet=412MB (Peak=455MB), Private=520MB, ManagedHeap=85MB, GCCommitted=110MB, GC0/1/2=120/40/8, GCPause=95ms, CPU=6.3%, Threads=42, Handles=1180, GDI=210, USER=95, Uptime=1.25h
```

| 欄位 | 說明 |
| :--- | :--- |
| **WorkingSet / Peak** | 目前與最高的實體記憶體用量 |
| **Private** | 程式自己佔用的記憶體，持續上升通常代表記憶體洩漏 |
| **ManagedHeap / GCCommitted** | .NET 物件用掉的記憶體 |
| **GC0/1/2、GCPause** | 記憶體回收的累計次數與累計暫停時間 |
| **CPU** | 與上一筆之間的平均 CPU 使用率 (已除以核心數) |
| **Threads / Handles** | 執行緒數量、系統資源代號數量 |
| **GDI / USER** | 視窗繪圖物件數量，各有 10000 的上限，到了就會畫不出來或當掉 |
| **Uptime** | 程式已執行的時數 |

長時間使用後，比較各筆的 Private、Handles、GDI 是否持續上升，即可判斷是否有資源洩漏。記錄間隔可修改 `ResourceMonitor.Interval`。


