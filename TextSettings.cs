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

        #region UI Brush Settings

        [Description("資料表格標題背景色: DataGrid column header background color. Default is '#FF4A4A4A'.")]
        public string DataGridColumnHeaderBackgroundColor { get; set; } = "#FF4A4A4A";

        [Description("資料表格標題前景色: DataGrid column header foreground color. Default is '#FFFFFFFF'.")]
        public string DataGridColumnHeaderForegroundColor { get; set; } = "#FFFFFFFF";


        [Description("主要顏色: Primary theme color for buttons, borders, highlights, and controls. Default is '#FFC107' (Amber).")]
        public string PrimaryColor { get; set; } = "#FFC107";


        [Description("主要淺色顏色: Primary light color for hover/focus states and singer tags. Default is '#FFECB3'.")]
        public string PrimaryLightColor { get; set; } = "#FFECB3";

        [Description("按鈕發光與選中強調色: Button glow and active highlight accent color. Default is empty (auto-derived from PrimaryColor).")]
        public string AccentColor { get; set; } = "";

        #endregion

    }
}
