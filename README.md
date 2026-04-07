# UltimateKtv

UltimateKtv 是一套針對 Windows 平台開發的專業 KTV 點歌與播放系統，支援雙螢幕獨立顯示、手機網頁端遠端點歌、YouTube 線上影音搜索播放以及硬體加速解碼等多項進階功能，為家庭娛樂提供完整的解決方案。

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


