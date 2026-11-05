using System;

namespace UltimateKtv
{
    /// <summary>
    /// 左側主選單的分類 (「設定」固定在最下方，不在此列)。順序即 themesettings.json NavMenuOrder 的預設順序。
    /// 新增分類：在 Items 加一列，並在 MainWindow.xaml 與 StyleSimulator 加上 Tag 為 Key 的按鈕。
    /// 主程式、StyleSimulator 與測試共用 (連結原始檔，不可引用只存在主程式的類別)。
    /// </summary>
    public static class NavMenu
    {
        /// <summary>
        /// 一個分類。Key 是 NavMenuOrder 用的名稱，也是按鈕的 Tag；DisplayName 給 StyleSimulator 顯示；
        /// GlowColor 是背景光暈色 (AmbientBackground)，null 用主題強調色。
        /// </summary>
        public sealed record Item(string Key, string DisplayName, string? GlowColor);

        public static readonly Item[] Items =
        {
            new("Rank", "排行", "#FF6A3D"),
            new("New", "新進", null),
            new("Singer", "歌星", "#9B7BFF"),
            new("Fav", "最愛", "#FF5C9A"),
            new("Num", "編號", "#4D8BFF"),
            new("Language", "語系", "#5BD68F"),
            new("Input", "搜尋", "#3FC1C9"),
            new("Youtube", "Youtube", "#FF4444"),
        };

        /// <summary>各分類的 Key，依預設順序。</summary>
        public static readonly string[] DefaultKeys = Array.ConvertAll(Items, i => i.Key);
    }
}
