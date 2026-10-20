using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// 一張交給 mpv 合成的點陣圖 (BGRA 預乘 alpha，放在非受控記憶體，mpv overlay-add 以位址讀取)。
    /// </summary>
    public sealed class OsdBitmap : IDisposable
    {
        public IntPtr Buffer { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public int Stride => Width * 4;

        private OsdBitmap(IntPtr buffer, int width, int height)
        {
            Buffer = buffer;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// 把 WPF 元素 (含外框、陰影等效果) 畫成點陣圖。必須在 UI 執行緒呼叫，元素要已完成版面配置。
        /// boundsInElement 回傳實際繪製範圍相對於元素左上角的位置 (DIP)，效果可能超出元素本身。
        /// </summary>
        public static OsdBitmap? Rasterize(Visual element, double scale, out Rect boundsInElement, double subPixelOffset = 0)
        {
            boundsInElement = VisualTreeHelper.GetDescendantBounds(element);
            if (boundsInElement.IsEmpty || boundsInElement.Width < 1 || boundsInElement.Height < 1) return null;

            // subPixelOffset (實體像素，0~1)：整張圖往右偏移，捲動時用來表現次像素位置；寬度多留 1px
            int w = (int)Math.Ceiling(boundsInElement.Width * scale) + (subPixelOffset > 0 ? 1 : 0);
            int h = (int)Math.Ceiling(boundsInElement.Height * scale);
            if (w <= 0 || h <= 0) return null;

            var dv = new DrawingVisual();
            // 元素本身就有繪圖內容 (例如 OutlinedTextBlock) 且沒有子元素/特效時，直接畫它的 Drawing。
            // 經由 VisualBrush 轉點陣圖時，沒有外框的文字 (DrawText 字形) 實測會變成空白。
            var ownDrawing = VisualTreeHelper.GetDrawing(element);
            bool simple = ownDrawing != null && VisualTreeHelper.GetChildrenCount(element) == 0
                          && (element as UIElement)?.Effect == null;
            using (var dc = dv.RenderOpen())
            {
                if (simple)
                {
                    dc.PushTransform(new TranslateTransform(-boundsInElement.X + subPixelOffset / scale, -boundsInElement.Y));
                    dc.DrawDrawing(ownDrawing);
                    dc.Pop();
                }
                else
                {
                    var brush = new VisualBrush(element)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = boundsInElement
                    };
                    dc.DrawRectangle(brush, null, new Rect(subPixelOffset / scale, 0, boundsInElement.Width, boundsInElement.Height));
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = w * 4;
            IntPtr buf = Marshal.AllocHGlobal(stride * h);
            rtb.CopyPixels(new Int32Rect(0, 0, w, h), buf, stride * h, stride);
            return new OsdBitmap(buf, w, h);
        }

        /// <summary>
        /// 畫一個不在畫面樹中的元素 (含 Effect)：先在 (0,0) 以指定寬度排版再直接 RenderTargetBitmap。
        /// 用在帶陰影的無外框文字 (歌詞)，避免 VisualBrush 路徑畫不出字形。
        /// </summary>
        public static OsdBitmap? RasterizeDetached(FrameworkElement element, double width, double scale)
        {
            element.Measure(new Size(width, double.PositiveInfinity));
            element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
            element.UpdateLayout();
            int w = (int)Math.Ceiling(width * scale);
            int h = (int)Math.Ceiling(element.DesiredSize.Height * scale);
            if (w <= 0 || h <= 0) return null;

            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(element);
            int stride = w * 4;
            IntPtr buf = Marshal.AllocHGlobal(stride * h);
            rtb.CopyPixels(new Int32Rect(0, 0, w, h), buf, stride * h, stride);
            return new OsdBitmap(buf, w, h);
        }

        /// <summary>全透明點陣圖 (用來預熱 mpv 的 OSD 著色器)。</summary>
        public static OsdBitmap CreateTransparent(int width, int height)
        {
            int size = width * 4 * height;
            IntPtr buf = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            return new OsdBitmap(buf, width, height);
        }

        public void Dispose()
        {
            if (Buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Buffer);
                Buffer = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// 一個顯示中的疊加項目。靜態項目固定位置；捲動項目由 MpvOsd 的動畫執行緒推動。
    /// 所有座標都是 mpv OSD 像素 (= 電視視窗實體像素)。
    /// </summary>
    public sealed class OsdItem
    {
        internal int Id;
        internal string? Slot; // 同一 slot 的新項目沿用同一 overlay id 原地換圖 (不會有空白或重疊的畫格)
        internal OsdBitmap Bitmap = null!;
        // 捲動用的次像素相位圖：Phases[i] 為往右偏移 i/Phases.Length 像素的版本 (Phases[0] == Bitmap)
        internal OsdBitmap[]? Phases;
        internal OsdBitmap? LastShownBitmap;
        internal int Y;
        // 靜態
        internal int X;
        // 捲動：文字左緣從 StartX 線性移動到 EndX，只在 [ClipLeft, ClipRight) 內可見
        internal bool IsScrolling;
        internal double StartX, EndX, DurationMs;
        internal int ClipLeft, ClipRight;
        internal int RepeatCount, HoldMs;
        internal long CycleStartTicks;
        internal bool Holding;
        internal long HoldUntilTicks;
        internal volatile int CompletedCycles;
        internal volatile bool Finished;
        internal Action? OnFinished;
        internal Dispatcher? Dispatcher;
        internal int LastShownX = int.MinValue, LastShownW = -1;
        internal int CountedAsScrolling; // 1 = 已計入 MpvOsd 的捲動中數量 (只扣一次)

        /// <summary>目前這一輪的進度 (0~1)，停頓中回傳 1。</summary>
        public double CurrentCycleProgress
        {
            get
            {
                if (!IsScrolling || Finished) return 0;
                if (Holding) return 1;
                double elapsed = (Stopwatch.GetTimestamp() - CycleStartTicks) * 1000.0 / Stopwatch.Frequency;
                return Math.Clamp(elapsed / DurationMs, 0, 1);
            }
        }

        public int CompletedCycleCount => CompletedCycles;
    }

    /// <summary>
    /// 把電視螢幕上的跑馬燈、文字、歌詞交給 mpv 合成 (overlay-add)。
    /// 捲動由背景執行緒以 DwmFlush 對齊螢幕更新來推動，完全不經過 WPF 的 UI 與渲染執行緒，
    /// 因此主控台忙碌 (換頁、開設定) 時電視上的跑馬燈不會停。
    /// </summary>
    public sealed class MpvOsd : IDisposable
    {
        private const int MaxOverlays = 64; // mpv overlay id 0..63

        private readonly Func<string[], int> _command;
        private readonly Func<string, string?> _getProperty;
        private readonly object _lock = new();
        private readonly Dictionary<int, OsdItem> _items = new();
        private readonly List<(OsdBitmap Bitmap, long Ticks)> _pendingFree = new();
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new(false);
        private volatile bool _disposed;
        private int _scrollingCount;

        /// <summary>捲動中的項目從無到有 (true) 或全部結束 (false) 時觸發；可能在 UI 或動畫執行緒呼叫。</summary>
        public event Action<bool>? ScrollingChanged;

        /// <summary>目前是否有捲動中的跑馬燈。</summary>
        public bool IsScrolling => Volatile.Read(ref _scrollingCount) > 0;

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        internal MpvOsd(Func<string[], int> command, Func<string, string?> getProperty)
        {
            _command = command;
            _getProperty = getProperty;
            _thread = new Thread(AnimationLoop) { IsBackground = true, Name = "mpv-osd", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>mpv OSD 的實體像素大小；沒有影像輸出時為 (0,0)。</summary>
        public Size OsdSize
        {
            get
            {
                double.TryParse(_getProperty("osd-width"), NumberStyles.Float, CultureInfo.InvariantCulture, out double w);
                double.TryParse(_getProperty("osd-height"), NumberStyles.Float, CultureInfo.InvariantCulture, out double h);
                return new Size(w, h);
            }
        }

        /// <param name="slot">同一個位置的提示 (例如右下角音量) 傳相同 slot：新圖直接取代舊圖、沿用同一個 overlay id。
        /// 先移除再加入時，中間只要剛好畫一格就會沒有提示 → 連續調音量時看起來閃爍。</param>
        public OsdItem? ShowStatic(OsdBitmap bitmap, int x, int y, string? slot = null)
        {
            var item = new OsdItem { Bitmap = bitmap, X = x, Y = y, Slot = slot };
            OsdItem? replaced = null;
            if (slot != null)
            {
                lock (_lock)
                {
                    foreach (var it in _items.Values)
                    {
                        if (it.Slot == slot && !it.IsScrolling) { replaced = it; break; }
                    }
                    if (replaced != null)
                    {
                        item.Id = replaced.Id;
                        _items[item.Id] = item;
                    }
                }
            }
            if (replaced != null)
            {
                lock (replaced) { replaced.Finished = true; }
                Apply(item, bitmap, x, 0, bitmap.Width); // 同一 id 的 overlay-add = 原地換圖
                DeferFree(replaced);
                return item;
            }
            if (!Register(item)) { bitmap.Dispose(); return null; }
            Apply(item, bitmap, x, 0, bitmap.Width);
            return item;
        }

        /// <param name="phases">次像素相位圖 (等分 0~1 像素的偏移)；overlay 位置只能是整數，慢速捲動時
        /// 1px/2px 交錯會看得出頓挫，依位置小數挑最接近的相位即可接近 WPF 的次像素移動。</param>
        public OsdItem? StartScroll(OsdBitmap[] phases, int y, double startX, double endX, double durationMs,
            int clipLeft, int clipRight, int repeatCount, int holdMs, Dispatcher dispatcher, Action onFinished)
        {
            var item = new OsdItem
            {
                Bitmap = phases[0], Phases = phases, Y = y, IsScrolling = true,
                StartX = startX, EndX = endX, DurationMs = Math.Max(1, durationMs),
                ClipLeft = clipLeft, ClipRight = clipRight,
                RepeatCount = Math.Max(1, repeatCount), HoldMs = Math.Max(50, holdMs),
                CycleStartTicks = Stopwatch.GetTimestamp(),
                Dispatcher = dispatcher, OnFinished = onFinished
            };
            if (!Register(item)) { foreach (var b in phases) b.Dispose(); return null; }
            item.CountedAsScrolling = 1;
            if (Interlocked.Increment(ref _scrollingCount) == 1) ScrollingChanged?.Invoke(true);
            _wake.Set();
            return item;
        }

        public void Remove(OsdItem? item)
        {
            if (item == null) return;
            lock (_lock)
            {
                // 已被同 slot 的新項目取代 (沿用同 id) 時不可移除，否則會把新提示一起拿掉
                if (!_items.TryGetValue(item.Id, out var current) || current != item) return;
                _items.Remove(item.Id);
            }
            if (MpvNative.Trace) AppLogger.Log($"[mpv-trace] osd remove id={item.Id} t={DateTime.Now:HH:mm:ss.fff}");
            // 與動畫執行緒的 Step 互斥，避免移除後又被 overlay-add 加回來
            lock (item)
            {
                item.Finished = true;
                _command(new[] { "overlay-remove", item.Id.ToString(CultureInfo.InvariantCulture) });
            }
            DeferFree(item);
            OnScrollEnded(item);
        }

        private void OnScrollEnded(OsdItem item)
        {
            if (Interlocked.Exchange(ref item.CountedAsScrolling, 0) == 1 &&
                Interlocked.Decrement(ref _scrollingCount) == 0)
            {
                ScrollingChanged?.Invoke(false);
            }
        }

        public void Clear()
        {
            List<OsdItem> all;
            lock (_lock) { all = new List<OsdItem>(_items.Values); }
            foreach (var item in all) Remove(item);
        }

        private bool Register(OsdItem item)
        {
            lock (_lock)
            {
                for (int id = 0; id < MaxOverlays; id++)
                {
                    if (!_items.ContainsKey(id))
                    {
                        item.Id = id;
                        _items[id] = item;
                        if (MpvNative.Trace) AppLogger.Log($"[mpv-trace] osd add id={id} scroll={item.IsScrolling} {item.Bitmap.Width}x{item.Bitmap.Height} t={DateTime.Now:HH:mm:ss.fff}");
                        return true;
                    }
                }
            }
            AppLogger.Log("[mpv-osd] No free overlay id; item dropped.");
            return false;
        }

        /// <summary>
        /// 把點陣圖的 [srcX, srcX+w) 欄位顯示在 OSD 的 x 位置 (用 offset 直接指向原記憶體，不需複製)。
        /// </summary>
        private void Apply(OsdItem item, OsdBitmap bmp, int x, int srcX, int w)
        {
            if (w <= 0)
            {
                if (item.LastShownW != 0)
                {
                    _command(new[] { "overlay-remove", item.Id.ToString(CultureInfo.InvariantCulture) });
                    item.LastShownW = 0;
                }
                return;
            }
            if (x == item.LastShownX && w == item.LastShownW && bmp == item.LastShownBitmap) return;
            _command(new[]
            {
                "overlay-add",
                item.Id.ToString(CultureInfo.InvariantCulture),
                x.ToString(CultureInfo.InvariantCulture),
                item.Y.ToString(CultureInfo.InvariantCulture),
                "&" + ((long)bmp.Buffer).ToString(CultureInfo.InvariantCulture),
                (srcX * 4).ToString(CultureInfo.InvariantCulture),
                "bgra",
                w.ToString(CultureInfo.InvariantCulture),
                bmp.Height.ToString(CultureInfo.InvariantCulture),
                bmp.Stride.ToString(CultureInfo.InvariantCulture)
            });
            item.LastShownX = x;
            item.LastShownW = w;
            item.LastShownBitmap = bmp;
        }

        private void AnimationLoop()
        {
            var active = new List<OsdItem>();
            while (!_disposed)
            {
                active.Clear();
                lock (_lock)
                {
                    foreach (var it in _items.Values)
                        if (it.IsScrolling && !it.Finished) active.Add(it);
                }

                if (active.Count == 0)
                {
                    FreePending();
                    _wake.WaitOne(500); // 沒有捲動項目時不佔 CPU
                    continue;
                }

                // 等下一次螢幕合成 (vsync)，讓位置更新與 mpv 的 display-resample 重畫節奏一致
                if (DwmFlush() != 0) Thread.Sleep(15);

                long now = Stopwatch.GetTimestamp();
                foreach (var it in active)
                {
                    try { lock (it) { if (!it.Finished) Step(it, now); } }
                    catch (Exception ex) { AppLogger.LogError("[mpv-osd] animation step failed", ex); }
                }
                if (MpvNative.Trace) TraceOsd(now);
                FreePending();
            }
        }

        private void Step(OsdItem it, long now)
        {
            if (it.Holding)
            {
                if (now < it.HoldUntilTicks) return;
                it.Holding = false;
                it.CycleStartTicks = now;
            }

            double elapsedMs = (now - it.CycleStartTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= it.DurationMs)
            {
                it.CompletedCycles++;
                if (it.CompletedCycles < it.RepeatCount)
                {
                    // 本輪結束 (文字已移出畫面)，停頓後重新從右邊進來
                    Apply(it, it.Bitmap, 0, 0, 0);
                    it.Holding = true;
                    it.HoldUntilTicks = now + (long)(it.HoldMs * Stopwatch.Frequency / 1000.0);
                    return;
                }

                // 全部輪數完成：移除並通知 UI (MarqueeManager 的佇列邏輯在 UI 執行緒)
                bool removed;
                lock (_lock) { removed = _items.Remove(it.Id); }
                it.Finished = true;
                if (removed)
                {
                    _command(new[] { "overlay-remove", it.Id.ToString(CultureInfo.InvariantCulture) });
                    DeferFree(it);
                }
                OnScrollEnded(it);
                var cb = it.OnFinished;
                if (cb != null) it.Dispatcher?.BeginInvoke(cb);
                return;
            }

            double x = it.StartX + (it.EndX - it.StartX) * (elapsedMs / it.DurationMs);
            int left = (int)Math.Floor(x);
            var bmp = it.Bitmap;
            if (it.Phases != null && it.Phases.Length > 1)
            {
                int phase = (int)Math.Round((x - left) * it.Phases.Length);
                if (phase >= it.Phases.Length) { phase = 0; left++; }
                bmp = it.Phases[phase];
            }
            else
            {
                left = (int)Math.Round(x);
            }
            int visL = Math.Max(left, it.ClipLeft);
            int visR = Math.Min(left + bmp.Width, it.ClipRight);
            Apply(it, bmp, visL, visL - left, visR - visL);
        }

        // overlay-remove 之後 mpv 可能還在讀舊記憶體，延到下一輪迴圈才釋放
        private void DeferFree(OsdItem item)
        {
            long now = Stopwatch.GetTimestamp();
            lock (_pendingFree)
            {
                if (item.Phases != null) foreach (var b in item.Phases) _pendingFree.Add((b, now));
                else _pendingFree.Add((item.Bitmap, now));
            }
        }


        private long _traceLast, _traceStart; private int _traceFrames, _traceSlow; private double _traceMaxGap, _traceMaxWork;

        private void TraceOsd(long now)
        {
            long end = Stopwatch.GetTimestamp();
            double gap = _traceLast == 0 ? 0 : (now - _traceLast) * 1000.0 / Stopwatch.Frequency;
            double work = (end - now) * 1000.0 / Stopwatch.Frequency;
            _traceLast = now; _traceFrames++;
            _traceMaxGap = Math.Max(_traceMaxGap, gap); _traceMaxWork = Math.Max(_traceMaxWork, work);
            if (gap > 25) { _traceSlow++; AppLogger.Log($"[mpv-trace] osd gap {gap:F0}ms t={DateTime.Now:HH:mm:ss.fff} gc0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} gcPauseTotal={GC.GetTotalPauseDuration().TotalMilliseconds:F0}ms lastGcPause={(GC.GetGCMemoryInfo(GCKind.Any).PauseDurations.Length > 0 ? GC.GetGCMemoryInfo(GCKind.Any).PauseDurations[0].TotalMilliseconds : 0):F1}ms gen={GC.GetGCMemoryInfo(GCKind.Any).Generation}"); }
            if (_traceStart == 0) _traceStart = now;
            if (now - _traceStart > 5 * Stopwatch.Frequency)
            {
                AppLogger.Log($"[mpv-trace] osd 5s: updates={_traceFrames} maxGap={_traceMaxGap:F0}ms gaps>25ms={_traceSlow} maxOverlayAddTime={_traceMaxWork:F1}ms gc0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
                _traceFrames = 0; _traceMaxGap = 0; _traceMaxWork = 0; _traceSlow = 0; _traceStart = now;
            }
        }
        private void FreePending()
        {
            long cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency; // 1 秒前
            List<OsdBitmap>? toFree = null;
            lock (_pendingFree)
            {
                for (int i = _pendingFree.Count - 1; i >= 0; i--)
                {
                    if (_pendingFree[i].Ticks > cutoff) continue;
                    (toFree ??= new List<OsdBitmap>()).Add(_pendingFree[i].Bitmap);
                    _pendingFree.RemoveAt(i);
                }
            }
            if (toFree != null) foreach (var b in toFree) b.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
            _wake.Set();
            _thread.Join(1000);
            lock (_pendingFree)
            {
                foreach (var (b, _) in _pendingFree) b.Dispose();
                _pendingFree.Clear();
            }
        }
    }
}
