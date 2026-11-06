using System;
using System.Windows;
using System.Windows.Controls;

namespace UltimateKtv
{
    /// <summary>
    /// 正在播放列「歌名 / 歌手」的橫向排版。子元素依序為歌名、分隔符、歌手。
    /// 全部放得下就照原寬顯示；放不下時歌手至少保留 <see cref="SingerReservedChars"/> 個中文字寬，
    /// 歌名較短而有剩餘空間時歌手可以再加寬，其餘給歌名 (歌名、歌手各自以 TextTrimming 截斷)。
    /// </summary>
    public class NowPlayingTitlePanel : Panel
    {
        public static readonly DependencyProperty SingerReservedCharsProperty =
            DependencyProperty.Register(nameof(SingerReservedChars), typeof(int), typeof(NowPlayingTitlePanel),
                new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>空間不足時歌手至少保留幾個中文字寬 (中文字寬 = 字級)。</summary>
        public int SingerReservedChars
        {
            get => (int)GetValue(SingerReservedCharsProperty);
            set => SetValue(SingerReservedCharsProperty, value);
        }

        // 各子元素的排版寬度；_advances 是下一個子元素的起點位移 (歌名截斷後 TextBlock 的 DesiredSize 是含 … 的實際寬度，
        // 比給它的寬度窄，分隔符緊接在 … 後面，不留空隙)
        private double[] _widths = Array.Empty<double>();
        private double[] _advances = Array.Empty<double>();

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = InternalChildren.Count;
            _widths = new double[count];
            double height = 0;
            double total = 0;
            var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
            for (int i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(unbounded);
                _widths[i] = child.DesiredSize.Width;
                total += _widths[i];
                height = Math.Max(height, child.DesiredSize.Height);
            }

            if (count == 3 && total > availableSize.Width)
            {
                double song = _widths[0], separator = _widths[1], singer = _widths[2];
                double fontSize = InternalChildren[2] is TextBlock tb ? tb.FontSize : 0;
                // 多留一點避免剛好 N 個字也被截斷
                double reserved = Math.Min(singer, fontSize * SingerReservedChars + 2);
                double singerWidth = Math.Min(singer, Math.Max(reserved, availableSize.Width - song - separator));
                double songWidth = Math.Max(0, availableSize.Width - separator - singerWidth);

                _widths[0] = songWidth;
                _widths[2] = singerWidth;
                InternalChildren[0].Measure(new Size(songWidth, availableSize.Height));
                InternalChildren[2].Measure(new Size(singerWidth, availableSize.Height));
            }

            _advances = new double[count];
            total = 0;
            for (int i = 0; i < count; i++)
            {
                _advances[i] = Math.Min(_widths[i], InternalChildren[i].DesiredSize.Width);
                total += _advances[i];
            }
            return new Size(Math.Min(total, availableSize.Width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                double w = i < _widths.Length ? _widths[i] : InternalChildren[i].DesiredSize.Width;
                // 以量測時的寬度排版 (避免換寬度重新截斷少一個字)，位移只用實際文字寬度
                InternalChildren[i].Arrange(new Rect(x, 0, w, finalSize.Height));
                x += i < _advances.Length ? _advances[i] : w;
            }
            return finalSize;
        }
    }
}
