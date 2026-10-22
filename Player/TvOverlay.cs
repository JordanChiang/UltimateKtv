using System;
using System.Windows;
using System.Windows.Threading;

namespace UltimateKtv
{
    /// <summary>疊加在電視畫面上的一個項目；交回 <see cref="ITvOverlay.Remove"/> 移除。</summary>
    public sealed class TvOverlayItem
    {
        internal TvOverlayItem(OsdItem item) => Item = item;
        internal OsdItem Item { get; }

        /// <summary>捲動項目目前這一輪的進度 (0~1)。</summary>
        public double CurrentCycleProgress => Item.CurrentCycleProgress;

        /// <summary>捲動項目已完成的輪數。</summary>
        public int CompletedCycleCount => Item.CompletedCycleCount;
    }

    /// <summary>
    /// 電視畫面的疊加層：把電視視窗裡的 WPF 元素 (跑馬燈、歌詞) 疊在影片上。
    /// <para>
    /// 位置一律以元素在電視視窗中的 WPF 版面為準；換算成影片像素、點陣化、捲動都在這裡處理。
    /// 方法回傳 null 表示這個疊加層不合成，呼叫端照常在 WPF 顯示該元素。
    /// </para>
    /// </summary>
    public interface ITvOverlay
    {
        /// <summary>true：元素由疊加層合成，WPF 中的元素應隱藏 (只保留版面)。</summary>
        bool IsComposited { get; }

        /// <summary>依 element 目前在電視視窗的位置顯示一張靜態圖。</summary>
        /// <param name="slot">同一位置的提示 (例如角落音量) 傳相同 slot：新圖原地取代舊圖，避免閃爍。</param>
        TvOverlayItem? ShowStatic(FrameworkElement element, string? slot = null);

        /// <summary>
        /// 顯示不在視覺樹中的元素 (例如加了留白的歌詞複本)，左上角對齊 anchor 的位置加上 offset。
        /// </summary>
        TvOverlayItem? ShowDetached(FrameworkElement detached, double width, FrameworkElement anchor, Vector offset);

        /// <summary>
        /// 在 canvas 範圍內把 text 由右往左捲動 (裁切在 canvas 左右邊界)，完成 repeatCount 次後呼叫 onFinished。
        /// </summary>
        /// <param name="y">text 在 canvas 內的 Y 位置。</param>
        TvOverlayItem? StartScroll(FrameworkElement text, FrameworkElement canvas, double y, double textWidth,
            double durationMs, int repeatCount, int holdMs, Dispatcher dispatcher, Action onFinished);

        void Remove(TvOverlayItem? item);
    }

    /// <summary>DirectShow：影片是 WPF 元素，跑馬燈/歌詞直接疊在 WPF 上，不需要合成。</summary>
    public sealed class WpfTvOverlay : ITvOverlay
    {
        public static readonly WpfTvOverlay Instance = new();

        private WpfTvOverlay() { }

        public bool IsComposited => false;
        public TvOverlayItem? ShowStatic(FrameworkElement element, string? slot = null) => null;
        public TvOverlayItem? ShowDetached(FrameworkElement detached, double width, FrameworkElement anchor, Vector offset) => null;
        public TvOverlayItem? StartScroll(FrameworkElement text, FrameworkElement canvas, double y, double textWidth,
            double durationMs, int repeatCount, int holdMs, Dispatcher dispatcher, Action onFinished) => null;
        public void Remove(TvOverlayItem? item) { }
    }

    /// <summary>
    /// libmpv：影片是原生視窗，WPF 疊不上去；把元素畫成點陣圖交給 mpv OSD 合成。
    /// root 的左上角對齊 mpv OSD 的 (0,0)。
    /// </summary>
    public sealed class MpvTvOverlay : ITvOverlay
    {
        // 次像素相位數 (0, 0.25, 0.5, 0.75 px)：捲動時依位置小數挑選，慢速也能平順
        private const int ScrollPhaseCount = 4;

        private readonly MpvOsd _osd;
        private readonly FrameworkElement _root;

        public MpvTvOverlay(MpvOsd osd, FrameworkElement root)
        {
            _osd = osd;
            _root = root;
        }

        public bool IsComposited => true;

        public TvOverlayItem? ShowStatic(FrameworkElement element, string? slot = null)
        {
            if (!TryGetScale(out double scale)) return null;
            element.UpdateLayout();
            var bmp = OsdBitmap.Rasterize(element, scale, out Rect bounds);
            if (bmp == null) return null;
            var origin = element.TranslatePoint(new Point(0, 0), _root);
            return Wrap(_osd.ShowStatic(bmp,
                (int)Math.Round((origin.X + bounds.X) * scale),
                (int)Math.Round((origin.Y + bounds.Y) * scale),
                slot));
        }

        public TvOverlayItem? ShowDetached(FrameworkElement detached, double width, FrameworkElement anchor, Vector offset)
        {
            if (!TryGetScale(out double scale)) return null;
            anchor.UpdateLayout();
            var bmp = OsdBitmap.RasterizeDetached(detached, width, scale);
            if (bmp == null) return null;
            var origin = anchor.TranslatePoint(new Point(0, 0), _root) + offset;
            return Wrap(_osd.ShowStatic(bmp,
                (int)Math.Round(origin.X * scale),
                (int)Math.Round(origin.Y * scale)));
        }

        public TvOverlayItem? StartScroll(FrameworkElement text, FrameworkElement canvas, double y, double textWidth,
            double durationMs, int repeatCount, int holdMs, Dispatcher dispatcher, Action onFinished)
        {
            if (!TryGetScale(out double scale)) return null;
            canvas.UpdateLayout();
            var origin = canvas.TranslatePoint(new Point(0, 0), _root);
            double canvasWidth = canvas.ActualWidth;

            var phases = new OsdBitmap[ScrollPhaseCount];
            Rect bounds = default;
            for (int i = 0; i < ScrollPhaseCount; i++)
            {
                var b = OsdBitmap.Rasterize(text, scale, out bounds, (double)i / ScrollPhaseCount);
                if (b == null)
                {
                    for (int j = 0; j < i; j++) phases[j].Dispose();
                    return null;
                }
                phases[i] = b;
            }

            return Wrap(_osd.StartScroll(phases,
                y: (int)Math.Round((origin.Y + y + bounds.Y) * scale),
                startX: (origin.X + canvasWidth + bounds.X) * scale,
                endX: (origin.X - textWidth + bounds.X) * scale,
                durationMs: durationMs,
                clipLeft: (int)Math.Round(origin.X * scale),
                clipRight: (int)Math.Round((origin.X + canvasWidth) * scale),
                repeatCount: repeatCount,
                holdMs: holdMs,
                dispatcher: dispatcher,
                onFinished: onFinished));
        }

        public void Remove(TvOverlayItem? item)
        {
            if (item != null) _osd.Remove(item.Item);
        }

        /// <summary>WPF 版面對應 OSD 像素的比例；OSD 尚未有影像輸出時回傳 false。</summary>
        private bool TryGetScale(out double scale)
        {
            scale = 0;
            if (_root.ActualWidth <= 0) return false;
            var osdSize = _osd.OsdSize;
            if (osdSize.Width <= 0) return false;
            scale = osdSize.Width / _root.ActualWidth;
            return true;
        }

        private static TvOverlayItem? Wrap(OsdItem? item) => item == null ? null : new TvOverlayItem(item);
    }
}
