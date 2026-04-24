using System.ComponentModel;

namespace UltimateKtv
{
    /// <summary>
    /// Defines the structure for application settings, to be serialized to/from JSON.
    /// </summary>
    public class AppSettings
    {
        [Description("點播螢幕顯示: 決定是否在主控台顯示影片預覽視窗。")]
        public bool ShowPreviewInMainWindow { get; set; } = true;

        [Description("新進歌曲天數: 設定幾天內的歌曲被視為「新進歌曲」。")]
        public int NewSongDays { get; set; } = 180;

        [Description("保持全螢幕播放: 若設為是，影片將拉伸填滿螢幕；否則保留原始長寬比。")]
        public bool IsMediaFullScreen { get; set; } = true;

        [Description("點播次數更新: 歌曲播放進度達到多少百分比時，增加一次點播次數。")]
        public int PlayCountUpdatePercentage { get; set; } = 35;

        [Description("音響增益值: 全域音訊增益設定 (100-500)。預設值為 200。")]
        public int AudioAmplify { get; set; } = 200;

        [Description("音量曲線: 設定音量調整的曲線模式。0=線性 (Linear), 1=指數 (Exponential - 預設), 2=對數 (Logarithmic)。有效範圍: 0 - 2。")]
        public int SoundCurve { get; set; } = 1;

        [Description("音量曲線指數: 設定指數曲線的形狀 (僅適用於指數模式)。預設值為 0.1 (數值越小起始音量越大，數值越大起始音量越小)。有效範圍: 0.1 - 2。")]
        public double SoundCurveExponent { get; set; } = 0.6;

        [Description("主控台螢幕: 主程式視窗顯示的螢幕編號 (Monitor Index)。")]
        public int ConsoleScreen { get; set; } = 1; // 1 for primary

        [Description("播放器螢幕: 影片播放視窗 (第二螢幕) 顯示的螢幕編號。")]
        public int PlayerScreen { get; set; } = 2; // 2 for secondary

        [Description("啟用遠端伺服器: 啟用用於遠端控制的 HTTP 伺服器。")]
        public bool EnableHttpServer { get; set; } = true;

        [Description("遠端伺服器IP: HTTP 伺服器的 IP 位址。設定 0.0.0.0 以監聽所有介面。")]
        public string HttpServerIp { get; set; } = "0.0.0.0";

        [Description("遠端伺服器埠號: HTTP 伺服器的連接埠號 (Port)。")]
        public int HttpServerPort { get; set; } = 8080;

        [Description("限制游標於主螢幕: 啟用時，將 Windows 滑鼠游標限制在主控台螢幕 (螢幕 1)範圍內。")]
        public bool LimitCursorOnMain { get; set; } = true;

        [Description("播放記錄自動保存次數: 在資料庫中保留歌曲播放歷史的次數 (1-10)。預設為 5。")]
        public int PlayHistoryCount { get; set; } = 5;

        [Description("歌曲排序方式: 歌曲排序方法。1=點播次數, 2=加歌日期, 3=字數。預設為 3。")]
        public int SongSortMethod { get; set; } = 1;

        [Description("APP記錄檔: 啟用或停用應用程式記錄功能。預設為 true (啟用)。")]
        public bool IsAppLoggerEnabled { get; set; } = true;

        [Description("視訊渲染器: 0=VMR9, 1=EVR。預設為 0 (VMR9)。")]
        public int VideoRendererType { get; set; } = 0;

        [Description("音訊輸出裝置: 所選音訊渲染裝置的名稱。預設為 'Default DirectSound Device'。")]
        public string AudioRendererDevice { get; set; } = "Default DirectSound Device";

        [Description("啟用硬體解碼: 是否啟用硬體加速功能。預設為 false。")]
        public bool EnableHWAccel { get; set; } = false;

        [Description("硬體解碼方式: 硬體加速模式。0=自動偵測, 1=NVIDIA, 2=Intel, 3=DXVA2(cb), 4=DXVA2(native), 5=D3D11。預設為 0。")]
        public int HWAccelMode { get; set; } = 0;

        [Description("公網伺服器埠號: 用於公網 IP QR Code 的連接埠號。預設為 8088。")]
        public int PublicServerPort { get; set; } = 8088;

        [Description("預先讀取: 啟用將候播清單的第一首歌曲預先載入到記憶體，以供網路存取。預設為 false。")]
        public bool EnablePreLoading { get; set; } = false;

        [Description("網路點播使用者記名: 啟用網路遠端點歌的使用者名稱記錄。預設為 false (停用)。")]
        public bool NetworkRemoteSongUsername { get; set; } = false;

        [Description("更換歌庫磁碟代號: 啟用變更歌庫磁碟路徑的功能。預設為 false (停用)。")]
        public bool ChangeSongLibraryDriveEnabled { get; set; } = false;

        [Description("歌庫磁碟路徑: 設定歌庫所在的磁碟代號與路徑。")]
        public string SongLibraryDrivePath { get; set; } = "";

        [Description("隨機播放: 啟用隨機播放模式。預設為 false (停用)。")]
        public bool RandomPlayEnabled { get; set; } = false;

        [Description("隨機播放類別: 隨機播放的類別。0=國語排行, 1=台語排行, 2=新進歌曲, 3=全部排行, 10=我的最愛。預設為 0。")]
        public int RandomPlayCategory { get; set; } = 0;

        [Description("隨機播放最愛用戶: 「我的最愛」隨機播放模式的指定用戶名稱。")]
        public string RandomPlayFavoriteUser { get; set; } = "";

        [Description("隨機播放聲道: 隨機播放時的音訊聲道。0=人聲, 1=伴唱。預設為 0。")]
        public int RandomPlayAudioChannel { get; set; } = 0;

        [Description("舊版聲道定義: 啟用舊版音訊聲道定義模式。預設為 true。, true=開啟, false=關閉)")]
        public bool IsLegacyAudioChannelDefinitionEnabled { get; set; } = true;

        [Description("網頁預設編號點歌: 網頁遠端服務啟動時直接跳轉至「編號點歌」。, true=開啟, false=關閉)")]
        public bool WebDefaultNumOrder { get; set; } = false;

        [Description("啟用視覺化歌星樣式: 啟用時，歌星選擇介面將顯示照片。, true=開啟, false=關閉)")]
        public bool VisualSingerStyle { get; set; } = true;

        [Description("歌星照片路徑: 設定歌星照片存放的目錄路徑。")]
        public string SingerPhotoPath { get; set; } = @"Images";

        [Description("視覺化歌星姓名大小: 設定視覺化歌星樣式時，歌星姓名的字體大小。")]
        public int VisualSingerNameFontSize { get; set; } = 26;

        [Description("Youtube 高畫質下載: 決定是否優先以高畫質 (1080p/720p) 下載 YouTube 影片。預設為 true。")]
        public bool HighQualityYoutube { get; set; } = true;

        [Description("Youtube 預設消除人聲: 啟動時是否預設將 YouTube 影片設定為消除人聲模式。預設為 true。")]
        public bool YoutubeDefaultNoVocal { get; set; } = true;

        [Description("Youtube 搜尋數量: 設定 YouTube 搜尋時回傳的影片數量。有效範圍: 10 - 100。預設值為 50。")]
        public int YoutubeSearchCount { get; set; } = 50;

        [Description("GitHub 更新網址: 設定自動更新檢查的 GitHub 倉庫 (格式: Owner/Repo)。預設為 JordanChiang/UltimateKtv。")]
        public string GitHubRepoUrl { get; set; } = "JordanChiang/UltimateKtv";
    }
}