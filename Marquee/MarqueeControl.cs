using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// A high-performance hardware-accelerated marquee control.
    /// Uses RenderTransform (TranslateTransform) to animate position entirely on the GPU,
    /// avoiding WPF layout passes (Measure/Arrange) and maintaining 60 FPS hardware VSync.
    /// </summary>
    public class MarqueeControl : UserControl, IDisposable
    {
        private Canvas _canvas = null!;
        private OutlinedTextBlock _textBlock = null!;
        private TranslateTransform _transform = null!;
        private DispatcherTimer? _holdTimer;
        private DispatcherTimer? _timeoutTimer;
        private int _currentRepeatCount = 0;
        private bool _isAnimating = false;

        private DateTime _cycleStartTime;
        private TimeSpan _cycleDuration;

        // OSD 模式 (libmpv)：電視影片是原生視窗，WPF 疊不上去，改把文字畫成點陣圖交給 mpv 合成與捲動。
        // 版面 (位置/大小) 仍由本控制項在 WPF 樹中計算，只是不顯示、也不跑 WPF 動畫。
        private OsdItem? _osdItem;

        /// <summary>不為 null 時以 mpv OSD 顯示 (由 MarqueeManager 在加入電視視窗時設定)。</summary>
        public MpvOsd? Osd { get; set; }

        /// <summary>座標基準元素，左上角對齊 mpv OSD 的 (0,0)。</summary>
        public FrameworkElement? OsdRoot { get; set; }

        // Public properties
        public string MarqueeText { get; set; } = string.Empty;
        public Brush TextColor { get; set; } = Brushes.White;
        public Brush StrokeColor { get; set; } = Brushes.Transparent;
        public double StrokeThickness { get; set; } = 0;
        public FontFamily TextFontFamily { get; set; } = new FontFamily("Microsoft JhengHei");
        public FontWeight TextFontWeight { get; set; } = FontWeights.Normal;
        public double TextFontSize { get; set; } = 24;
        public int RepeatCount { get; set; } = 1;
        public int HoldTimeMs { get; set; } = 500;
        public MarqueePosition Position { get; set; } = MarqueePosition.Bottom;
        public double Speed { get; set; } = 50;
        public int DesiredDisplayDevice { get; set; } = 0;
        public bool IsStaticMode { get; set; } = false;
        public int TimeoutSeconds { get; set; } = 0;
        public int RemainingRepeatCount => Math.Max(0, RepeatCount - (_osdItem != null ? _osdItem.CompletedCycleCount : _currentRepeatCount));

        /// <summary>
        /// Gets the scroll progress of the current cycle (0.0 to 1.0).
        /// Returns 1.0 if currently in the hold interval after completing a cycle.
        /// </summary>
        public double CurrentCycleProgress
        {
            get
            {
                if (!_isAnimating) return 0.0;
                if (_osdItem != null) return _osdItem.CurrentCycleProgress;
                if (_holdTimer != null) return 1.0;
                if (_cycleDuration.TotalMilliseconds <= 0) return 0.0;
                double elapsed = (DateTime.UtcNow - _cycleStartTime).TotalMilliseconds;
                return Math.Clamp(elapsed / _cycleDuration.TotalMilliseconds, 0.0, 1.0);
            }
        }

        public event EventHandler? MarqueeCompleted;

        public MarqueeControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Top;
            this.Margin = new Thickness(0);
            this.IsHitTestVisible = false;
            this.ClipToBounds = true;
            this.UseLayoutRounding = false;
            this.SnapsToDevicePixels = false;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
            RenderOptions.SetCachingHint(this, CachingHint.Cache);

            _canvas = new Canvas
            {
                ClipToBounds = true,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            RenderOptions.SetCachingHint(_canvas, CachingHint.Cache);

            _transform = new TranslateTransform();

            _textBlock = new OutlinedTextBlock
            {
                Fill = TextColor,
                Stroke = StrokeColor,
                StrokeThickness = StrokeThickness,
                FontFamily = TextFontFamily,
                FontWeight = TextFontWeight,
                FontSize = TextFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _transform,
                CacheMode = new BitmapCache
                {
                    EnableClearType = false,
                    SnapsToDevicePixels = false
                }
            };
            RenderOptions.SetCachingHint(_textBlock, CachingHint.Cache);

            Canvas.SetLeft(_textBlock, 0);
            Canvas.SetTop(_textBlock, 0);

            _canvas.Children.Add(_textBlock);
            this.Content = _canvas;
        }

        public void UpdateMarquee(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, int displayDevice,
            FontWeight? fontWeight = null, Brush? strokeColor = null, double strokeThickness = 0, int holdTimeMs = 500)
        {
            bool wasAnimating = _isAnimating;
            if (wasAnimating) StopMarquee();

            MarqueeText          = text;
            TextColor            = color;
            StrokeColor          = strokeColor ?? Brushes.Transparent;
            StrokeThickness      = strokeThickness;
            TextFontFamily       = fontFamily;
            TextFontWeight       = fontWeight ?? FontWeights.Normal;
            TextFontSize         = fontSize;
            RepeatCount          = repeatCount;
            HoldTimeMs           = holdTimeMs;
            Position             = position;
            Speed                = speed;
            DesiredDisplayDevice = displayDevice;
            IsStaticMode         = false;

            if (wasAnimating && !string.IsNullOrEmpty(text)) StartMarquee();
        }

        public void UpdateStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, int displayDevice,
            FontWeight? fontWeight = null, Brush? strokeColor = null, double strokeThickness = 0)
        {
            StopMarquee();
            MarqueeText          = text;
            TextColor            = color;
            StrokeColor          = strokeColor ?? Brushes.Transparent;
            StrokeThickness      = strokeThickness;
            TextFontFamily       = fontFamily;
            TextFontWeight       = fontWeight ?? FontWeights.Normal;
            TextFontSize         = fontSize;
            Position             = position;
            TimeoutSeconds       = timeoutSeconds;
            DesiredDisplayDevice = displayDevice;
            IsStaticMode         = true;
        }

        public void EnsureVisible()
        {
            this.Visibility = Visibility.Visible;
            _canvas.Visibility = Visibility.Visible;
            _textBlock.Visibility = Visibility.Visible;
            this.Opacity = 1.0;
            Panel.SetZIndex(this, 100);
        }

        public void StartMarquee()
        {
            if (string.IsNullOrEmpty(MarqueeText) || _isAnimating) return;

            if (ActualWidth == 0 || ActualHeight == 0)
            {
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Arrange(new Rect(0, 0, Width > 0 ? Width : 800, Height > 0 ? Height : 80));
                UpdateLayout();

                if (ActualWidth == 0 || ActualHeight == 0)
                {
                    Dispatcher.BeginInvoke(new Action(StartMarqueeInternal), DispatcherPriority.Loaded);
                    return;
                }
            }

            StartMarqueeInternal();
        }

        private void StartMarqueeInternal()
        {
            ApplyTextProperties();

            _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = _textBlock.DesiredSize.Width;
            double textHeight = _textBlock.DesiredSize.Height;

            double canvasWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 800);
            double canvasHeight = Math.Max(textHeight, ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 80));

            _canvas.Width = canvasWidth;
            _canvas.Height = canvasHeight;

            double startY = Position switch
            {
                MarqueePosition.Top or MarqueePosition.TopLeft or MarqueePosition.TopRight => 0,
                MarqueePosition.Center => Math.Max(0, (canvasHeight - textHeight) / 2),
                _ => Math.Max(0, canvasHeight - textHeight - 5)
            };

            Canvas.SetLeft(_textBlock, 0);
            Canvas.SetTop(_textBlock, startY);

            _currentRepeatCount = 0;
            _isAnimating = true;

            if (Osd != null && StartOsdScroll(canvasWidth, textWidth, startY)) return;
            StartScrollAnimation(canvasWidth, textWidth);
        }

        /// <summary>
        /// 計算 WPF 版面對應的 OSD 像素比例與畫布原點；OSD 尚未有影像輸出時回傳 false。
        /// </summary>
        private bool TryGetOsdGeometry(out double scale, out Point canvasOrigin)
        {
            scale = 0;
            canvasOrigin = default;
            if (Osd == null || OsdRoot == null || OsdRoot.ActualWidth <= 0) return false;
            var osdSize = Osd.OsdSize;
            if (osdSize.Width <= 0) return false;
            // OSD 模式不在 WPF 顯示文字，不需要 BitmapCache；而且元素還沒被 WPF 畫過時快取是空的，
            // 透過 VisualBrush 轉點陣圖會得到空白 (音量等角落提示實測如此)
            _textBlock.CacheMode = null;
            UpdateLayout();
            scale = osdSize.Width / OsdRoot.ActualWidth;
            canvasOrigin = _canvas.TranslatePoint(new Point(0, 0), OsdRoot);
            return true;
        }

        private bool StartOsdScroll(double canvasWidth, double textWidth, double startY)
        {
            if (!TryGetOsdGeometry(out double scale, out Point origin)) return false;
            // 4 個次像素相位 (0, 0.25, 0.5, 0.75 px)：捲動時依位置小數挑選，慢速也能平順
            const int PhaseCount = 4;
            var phases = new OsdBitmap[PhaseCount];
            Rect bounds = default;
            for (int i = 0; i < PhaseCount; i++)
            {
                var b = OsdBitmap.Rasterize(_textBlock, scale, out bounds, (double)i / PhaseCount);
                if (b == null)
                {
                    for (int j = 0; j < i; j++) phases[j].Dispose();
                    return false;
                }
                phases[i] = b;
            }

            double speed = Math.Max(1.0, Speed);
            double durationMs = (canvasWidth + textWidth) / speed * 1000.0;
            _cycleStartTime = DateTime.UtcNow;
            _cycleDuration = TimeSpan.FromMilliseconds(durationMs);

            _osdItem = Osd!.StartScroll(phases,
                y: (int)Math.Round((origin.Y + startY + bounds.Y) * scale),
                startX: (origin.X + canvasWidth + bounds.X) * scale,
                endX: (origin.X - textWidth + bounds.X) * scale,
                durationMs: durationMs,
                clipLeft: (int)Math.Round(origin.X * scale),
                clipRight: (int)Math.Round((origin.X + canvasWidth) * scale),
                repeatCount: RepeatCount,
                holdMs: HoldTimeMs,
                dispatcher: Dispatcher,
                onFinished: OnOsdScrollFinished);
            if (_osdItem == null) return false;

            _textBlock.Visibility = Visibility.Hidden; // 保留版面，但不在 WPF 繪製
            return true;
        }

        private void OnOsdScrollFinished()
        {
            if (!_isAnimating || _osdItem == null) return;
            _osdItem = null;
            _isAnimating = false;
            _currentRepeatCount = RepeatCount;
            MarqueeCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void StartScrollAnimation(double canvasWidth, double textWidth)
        {
            if (!_isAnimating) return;

            double startX = canvasWidth;
            double endX = -textWidth;
            double distance = startX - endX;
            double speed = Math.Max(1.0, Speed);
            var duration = TimeSpan.FromSeconds(distance / speed);

            _cycleStartTime = DateTime.UtcNow;
            _cycleDuration = duration;

            var anim = new DoubleAnimation
            {
                From = startX,
                To = endX,
                Duration = duration,
                EasingFunction = null
            };

            anim.Completed += (s, e) =>
            {
                if (!_isAnimating) return;

                _currentRepeatCount++;
                if (_currentRepeatCount < RepeatCount)
                {
                    int holdMs = Math.Max(50, HoldTimeMs);
                    _holdTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(holdMs)
                    };
                    _holdTimer.Tick += (_, __) =>
                    {
                        _holdTimer?.Stop();
                        _holdTimer = null;
                        if (_isAnimating)
                        {
                            StartScrollAnimation(canvasWidth, textWidth);
                        }
                    };
                    _holdTimer.Start();
                }
                else
                {
                    _isAnimating = false;
                    _transform.BeginAnimation(TranslateTransform.XProperty, null);
                    _transform.X = endX;
                    MarqueeCompleted?.Invoke(this, EventArgs.Empty);
                }
            };

            _transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        public void StartStaticDisplay()
        {
            if (string.IsNullOrEmpty(MarqueeText)) return;

            if (ActualWidth == 0 || ActualHeight == 0)
            {
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Arrange(new Rect(0, 0, Width > 0 ? Width : 800, Height > 0 ? Height : 80));
                UpdateLayout();

                if (ActualWidth == 0 || ActualHeight == 0)
                {
                    Dispatcher.BeginInvoke(new Action(StartStaticDisplayInternal), DispatcherPriority.Loaded);
                    return;
                }
            }

            StartStaticDisplayInternal();
        }

        private void StartStaticDisplayInternal()
        {
            StopMarquee();
            ApplyTextProperties();

            _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = _textBlock.DesiredSize.Width;
            double textHeight = _textBlock.DesiredSize.Height;

            double canvasWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 800);
            double canvasHeight = Math.Max(textHeight, ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 80));

            _canvas.Width = canvasWidth;
            _canvas.Height = canvasHeight;

            double textY = Position switch
            {
                MarqueePosition.Top or MarqueePosition.TopLeft or MarqueePosition.TopRight => 5,
                MarqueePosition.Center => Math.Max(5, (canvasHeight - textHeight) / 2),
                _ => Math.Max(5, canvasHeight - textHeight - 5)
            };

            double textX = Position switch
            {
                MarqueePosition.TopLeft or MarqueePosition.BottomLeft => 10,
                MarqueePosition.TopRight or MarqueePosition.BottomRight => Math.Max(10, canvasWidth - textWidth - 10),
                _ => Math.Max(10, (canvasWidth - textWidth) / 2)
            };

            _transform.BeginAnimation(TranslateTransform.XProperty, null);
            _transform.X = 0;
            _transform.Y = 0;

            Canvas.SetLeft(_textBlock, textX);
            Canvas.SetTop(_textBlock, textY);

            _isAnimating = true;

            if (Osd != null && TryGetOsdGeometry(out double scale, out Point origin))
            {
                var bmp = OsdBitmap.Rasterize(_textBlock, scale, out Rect bounds);
                if (bmp != null)
                {
                    bool isCorner = Position is MarqueePosition.TopLeft or MarqueePosition.TopRight
                                    or MarqueePosition.BottomLeft or MarqueePosition.BottomRight;
                    _osdItem = Osd.ShowStatic(bmp,
                        (int)Math.Round((origin.X + textX + bounds.X) * scale),
                        (int)Math.Round((origin.Y + textY + bounds.Y) * scale),
                        slot: isCorner ? "corner:" + Position : null);
                    if (_osdItem != null) _textBlock.Visibility = Visibility.Hidden;
                }
            }

            if (TimeoutSeconds > 0)
            {
                _timeoutTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(TimeoutSeconds)
                };
                _timeoutTimer.Tick += (_, __) =>
                {
                    _timeoutTimer?.Stop();
                    _timeoutTimer = null;
                    _isAnimating = false;
                    MarqueeCompleted?.Invoke(this, EventArgs.Empty);
                };
                _timeoutTimer.Start();
            }
        }

        public void StopMarquee()
        {
            _isAnimating = false;
            if (_osdItem != null)
            {
                Osd?.Remove(_osdItem);
                _osdItem = null;
                _textBlock.Visibility = Visibility.Visible;
            }
            _holdTimer?.Stop();
            _holdTimer = null;
            _timeoutTimer?.Stop();
            _timeoutTimer = null;

            _transform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        public void Dispose()
        {
            StopMarquee();
        }


        private void ApplyTextProperties()
        {
            _textBlock.Text = MarqueeText;
            _textBlock.Fill = TextColor;
            _textBlock.Stroke = StrokeColor;
            _textBlock.StrokeThickness = StrokeThickness;
            _textBlock.FontFamily = TextFontFamily;
            _textBlock.FontWeight = TextFontWeight;
            _textBlock.FontSize = TextFontSize;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_isAnimating && !IsStaticMode)
            {
                StopMarquee();
                StartMarquee();
            }
        }
    }
}
