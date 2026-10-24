using System.ComponentModel;
using System.Text.Json.Serialization;

namespace UltimateKtv
{
    /// <summary>
    /// Defines the structure for UI theme and visual customization settings, to be serialized to/from JSON.
    /// Allows customization of UI theme colors, brushes, font sizes, colors, and song list row display styles.
    /// </summary>
    public class ThemeSettings
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

        [Description("資料表格標題背景色: DataGrid column header background color. Default is '#D28300' (與範本 theme_default_amber 一致).")]
        public string DataGridColumnHeaderBackgroundColor { get; set; } = "#D28300";

        [Description("資料表格標題前景色: DataGrid column header foreground color. Default is '#FFFFFFFF'.")]
        public string DataGridColumnHeaderForegroundColor { get; set; } = "#FFFFFFFF";


        [Description("主要顏色: Primary theme color for buttons, borders, highlights, and controls. Default is '#FFB300' (Amber).")]
        public string PrimaryColor { get; set; } = "#FFB300";


        [Description("主要淺色顏色: Primary light color for hover/focus states and singer tags. Default is '#FFE082'.")]
        public string PrimaryLightColor { get; set; } = "#FFE082";

        [Description("按鈕發光與選中強調色: Button glow and active highlight accent color. Default is empty (auto-derived from PrimaryColor).")]
        public string AccentColor { get; set; } = "";

        [Description("歌曲清單列外觀風格: 1=風格 1 (靜態微光光暈風格), 2=風格 2 (膠囊外框輪廓風格), 3=風格 3 (左側主色指示條卡片), 4=風格 4 (現代懸浮微圓角卡片), 5=風格 5 (霓虹底線), 6=風格 6 (糖果光澤), 7=風格 7 (雙色漸層框), 8=風格 8 (科技切角框).")]
        public int SongListRowStyle { get; set; } = 1;

        #endregion

        #region Nav Menu Settings

        /// <summary>左側主選單可排序的項目 (「設定」固定在最下方，不在此列)，同時也是預設順序。</summary>
        public static readonly string[] NavMenuDefaultKeys = { "Rank", "New", "Singer", "Fav", "Num", "Language", "Input", "Youtube" };

        [Description("左側主選單順序 (以逗號分隔，「設定」固定在最下方): Singer=歌星, Fav=最愛, Num=編號, Language=語系, New=新進, Rank=排行, Input=搜尋, Youtube=Youtube.")]
        public string NavMenuOrder { get; set; } = string.Join(",", NavMenuDefaultKeys);

        /// <summary>
        /// 解析 NavMenuOrder：忽略未知與重複的項目，缺少的項目依預設順序補在最後。
        /// </summary>
        public static List<string> ParseNavMenuOrder(string? order)
        {
            var result = new List<string>();
            foreach (var part in (order ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? key = Array.Find(NavMenuDefaultKeys, k => string.Equals(k, part, StringComparison.OrdinalIgnoreCase));
                if (key != null && !result.Contains(key)) result.Add(key);
            }
            foreach (var key in NavMenuDefaultKeys)
            {
                if (!result.Contains(key)) result.Add(key);
            }
            return result;
        }

        #endregion

        #region Validation

        /// <summary>
        /// 把超出範圍的字級拉回可用範圍 (與 StyleSimulator 的滑桿範圍相同)。
        /// 字級 0 或負數會讓 WPF 丟出例外而無法啟動。
        /// </summary>
        /// <returns>被修正的項目名稱。</returns>
        public List<string> Normalize()
        {
            var corrected = new List<string>();
            FuncBtnFontSize = ClampFontSize(nameof(FuncBtnFontSize), FuncBtnFontSize, 14, 64, corrected);
            BottomButtonFontSize = ClampFontSize(nameof(BottomButtonFontSize), BottomButtonFontSize, 14, 64, corrected);
            WaitingListFontSize = ClampFontSize(nameof(WaitingListFontSize), WaitingListFontSize, 12, 60, corrected);
            SongListFontSize = ClampFontSize(nameof(SongListFontSize), SongListFontSize, 16, 72, corrected);
            WebHostInfoFontSize = ClampFontSize(nameof(WebHostInfoFontSize), WebHostInfoFontSize, 12, 60, corrected);
            return corrected;
        }

        private static double ClampFontSize(string name, double value, double min, double max, List<string> corrected)
        {
            double clamped = Math.Clamp(value, min, max);
            if (clamped != value) corrected.Add(name);
            return clamped;
        }

        #endregion

    }
}
