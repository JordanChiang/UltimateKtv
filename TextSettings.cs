using System.ComponentModel;
using System.Text.Json.Serialization;

namespace UltimateKtv
{
    /// <summary>
    /// Defines the structure for text customization settings, to be serialized to/from JSON.
    /// Allows customization of text type, colors, and brush settings for marquee and display elements.
    /// </summary>
    public class TextSettings
    {
        #region Font Settings

        [Description("字型名稱: Font family name for display text. Default is 'Microsoft JhengHei'.")]
        public string FontFamily { get; set; } = "Microsoft JhengHei";

        [Description("功能按鈕字體大小(32)")]
        public double FuncBtnFontSize { get; set; } = 32;

        [Description("底部按鈕字體大小(36)")]
        public double BottomButtonFontSize { get; set; } = 36;

        [Description("待播清單字體大小(24)")]
        public double WaitingListFontSize { get; set; } = 24;

        [Description("歌曲清單字體大小(38)")]
        public double SongListFontSize { get; set; } = 38;

        #endregion

        #region Marquee Text Colors

        [Description("跑馬燈前景色: Foreground color for marquee text in hex format. Default is '#FFFFFF' (White).")]
        public string MarqueeForegroundColor { get; set; } = "#FFFFFF";

        [Description("跑馬燈速度: Marquee scrolling speed in pixels per second. Default is 100.")]
        public double MarqueeSpeed { get; set; } = 100;

        [Description("跑馬燈重複次數: Number of times marquee text repeats. Default is 2.")]
        public int MarqueeRepeatCount { get; set; } = 2;

        [Description("跑馬燈字型大小: Font size specifically for marquee text. Default is 52.")]
        public double MarqueeFontSize { get; set; } = 52;
        
        [Description("通知與次要跑馬燈字體大小 (系統跑馬燈、通知、音量顯示等)")]
        public double NotificationFontSize { get; set; } = 28;

        #endregion

        #region Announcement Text Colors

        [Description("公告前景色: Foreground color for announcement text in hex format. Default is '#FFFFFF' (White).")]
        public string AnnouncementForegroundColor { get; set; } = "#FFFFFF";

        #endregion

        #region Static Text Colors

        [Description("靜態文字前景色: Foreground color for static text display in hex format. Default is '#FFFFFF' (White).")]
        public string StaticTextForegroundColor { get; set; } = "#FFFFFF";

        #endregion

        #region Web Host Info Settings

        [Description("網路主機資訊字型大小: Font size for web host info display. Default is 32.")]
        public double WebHostInfoFontSize { get; set; } = 32;

        [Description("網路主機資訊前景色: Foreground color for web host info in hex format. Default is '#FFFFFF' (White).")]
        public string WebHostInfoForegroundColor { get; set; } = "#FFFFFF";

        #endregion

        #region Song Added Message Settings

        [Description("歌曲新增訊息字型大小: Font size for song added notification. Default is 24.")]
        public double SongAddedFontSize { get; set; } = 32;

        [Description("歌曲新增訊息前景色: Foreground color for song added notification in hex format. Default is '#90EE90' (Light Green).")]
        public string SongAddedForegroundColor { get; set; } = "#90EE90";

        #endregion


        #region UI Brush Settings

        [Description("資料表格標題背景色: DataGrid column header background color. Default is '#FF4A4A4A'.")]
        public string DataGridColumnHeaderBackgroundColor { get; set; } = "#FF4A4A4A";

        [Description("資料表格標題前景色: DataGrid column header foreground color. Default is '#FFFFFFFF'.")]
        public string DataGridColumnHeaderForegroundColor { get; set; } = "#FFFFFFFF";

        [Description("亮色邊框顏色: Bright border color for highlighted elements. Default is '#FFD700' (Gold).")]
        public string BrightBorderColor { get; set; } = "#FFD700";

        [Description("主要顏色: Primary theme color for borders and highlights. Default is '#FFC107' (Amber).")]
        public string PrimaryColor { get; set; } = "#FFC107";

        [Description("主要中階顏色: Primary mid-tone color. Default is '#FFB300'.")]
        public string PrimaryMidColor { get; set; } = "#FFB300";

        [Description("主要深色顏色: Primary dark color. Default is '#FF8F00'.")]
        public string PrimaryDarkColor { get; set; } = "#FF8F00";

        [Description("主要淺色顏色: Primary light color for hover/focus states. Default is '#FFECB3'.")]
        public string PrimaryLightColor { get; set; } = "#FFECB3";

        [Description("主要前景色: Foreground color on primary-colored buttons. Default is '#FFFFFF'.")]
        public string PrimaryForegroundColor { get; set; } = "#FFFFFF";


        #endregion

        #region Pitch Control Dialog Settings

        [Description("音調控制對話框背景色: Pitch control dialog background color. Default is '#424242'.")]
        public string PitchDialogBackgroundColor { get; set; } = "#424242";

        [Description("音調控制對話框前景色: Pitch control dialog foreground/text color. Default is '#FFFFFF'.")]
        public string PitchDialogForegroundColor { get; set; } = "#FFFFFF";

        [Description("音調控制按鈕背景色: Pitch control button background color. Default is null (uses PrimaryMidColor).")]
        public string? PitchDialogButtonBackgroundColor { get; set; } = null;

        [Description("音調控制按鈕前景色: Pitch control button foreground color. Default is null (uses PrimaryForegroundColor).")]
        public string? PitchDialogButtonForegroundColor { get; set; } = null;

        [Description("音調控制按鈕邊框色: Pitch control button border color. Default is null (uses PrimaryMidColor).")]
        public string? PitchDialogButtonBorderColor { get; set; } = null;

        [Description("音調顯示按鈕背景色: Current pitch display button background. Default is null (uses PrimaryMidColor).")]
        public string? PitchDisplayButtonBackgroundColor { get; set; } = null;

        #endregion

    }
}
