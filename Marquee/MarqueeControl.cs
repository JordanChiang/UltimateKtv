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
        public int RemainingRepeatCount => Math.Max(0, RepeatCount - _currentRepeatCount);

        /// <summary>
        /// Gets the scroll progress of the current cycle (0.0 to 1.0).
        /// Returns 1.0 if currently in the hold interval after completing a cycle.
        /// </summary>
        public double CurrentCycleProgress
        {
            get
            {
                if (!_isAnimating) return 0.0;
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

            StartScrollAnimation(canvasWidth, textWidth);
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
