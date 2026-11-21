using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 沒有樣式的字幕 (.srt) 要套用的外觀，沿用電視畫面 LRC 歌詞的樣子。
    /// 大小與底部距離以「佔畫面高度的比例」表示，各引擎依自己的座標換算。
    /// </summary>
    /// <param name="FontFamily">字型名稱 (單一字型)。</param>
    /// <param name="FontSizeRatio">字級 / 電視畫面高度。</param>
    /// <param name="MarginBottomRatio">字幕底部到畫面底部的距離 / 電視畫面高度。</param>
    /// <param name="ShadowOffsetRatio">陰影位移 / 電視畫面高度。</param>
    public sealed record SubtitleStyle(
        string FontFamily,
        double FontSizeRatio,
        double MarginBottomRatio,
        double ShadowOffsetRatio,
        Color Foreground,
        Color Shadow,
        bool Bold);
}
