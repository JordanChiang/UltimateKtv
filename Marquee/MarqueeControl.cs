using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// A customizable marquee control that displays scrolling text with various configuration options
    /// </summary>
    public class MarqueeControl : UserControl
    {
        private Canvas _canvas = null!;
        private TextBlock _textBlock = null!;
        private Storyboard? _scrollStoryboard;
        private DispatcherTimer _repeatTimer = null!;
        private int _currentRepeatCount = 0;
        private int _maxRepeatCount = 1;
        private bool _isAnimating = false;

        // Marquee properties
        public string MarqueeText { get; set; } = string.Empty;
        public Brush TextColor { get; set; } = Brushes.White;
        public FontFamily TextFontFamily { get; set; } = new FontFamily("Arial");
        public double TextFontSize { get; set; } = 24;
        public int RepeatCount { get; set; } = 1;
        public MarqueePosition Position { get; set; } = MarqueePosition.Bottom;
        public double Speed { get; set; } = 50; // pixels per second
        public int DesiredDisplayDevice { get; set; } = 0; // 0 = main window, 1+ = secondary displays
        
        // Static text properties
        public bool IsStaticMode { get; set; } = false;
        public int TimeoutSeconds { get; set; } = 0;
        private DispatcherTimer? _timeoutTimer;

        public MarqueeControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // CRITICAL FIX: Set explicit positioning to avoid NaN
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Top;
            this.Margin = new Thickness(0);
            
            // Force the UserControl to have a valid position
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, 0);
            Grid.SetRow(this, 0);
            Grid.SetColumn(this, 0);
            
            // Create the canvas container
            _canvas = new Canvas
            {
                ClipToBounds = true,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Create the text block
            _textBlock = new TextBlock
            {
                Foreground = TextColor,
                FontFamily = TextFontFamily,
                FontSize = TextFontSize,
                VerticalAlignment = VerticalAlignment.Center
            };

            // CRITICAL FIX: Set explicit Canvas positioning to avoid NaN
            Canvas.SetLeft(_textBlock, 0);
            Canvas.SetTop(_textBlock, 0);

            _canvas.Children.Add(_textBlock);
            Content = _canvas;

            // Initialize the repeat timer
            _repeatTimer = new DispatcherTimer();
            _repeatTimer.Tick += RepeatTimer_Tick!;
            
//            System.Diagnostics.Debug.WriteLine("MarqueeControl initialized with explicit positioning");
        }

        /// <summary>
        /// Starts the marquee animation with the current settings
        /// </summary>
        public void StartMarquee()
        {
            if (string.IsNullOrEmpty(MarqueeText) || _isAnimating)
            {
                System.Diagnostics.Debug.WriteLine($"StartMarquee aborted: Text empty={string.IsNullOrEmpty(MarqueeText)}, IsAnimating={_isAnimating}");
                return;
            }

 //           System.Diagnostics.Debug.WriteLine($"=== StartMarquee Called ===");
 //           System.Diagnostics.Debug.WriteLine($"MarqueeText: '{MarqueeText}'");
 //           System.Diagnostics.Debug.WriteLine($"Control Size: {Width}x{Height}");
 //           System.Diagnostics.Debug.WriteLine($"ActualSize: {ActualWidth}x{ActualHeight}");

            // Force layout update if ActualSize is 0
            if (ActualWidth == 0 || ActualHeight == 0)
            {
//                System.Diagnostics.Debug.WriteLine("ActualSize is 0, forcing layout update...");
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Arrange(new Rect(0, 0, Width > 0 ? Width : 404, Height > 0 ? Height : 80));
                UpdateLayout();
                
//                System.Diagnostics.Debug.WriteLine($"After layout update - ActualSize: {ActualWidth}x{ActualHeight}");
                
                // If still 0, use a dispatcher to delay execution
                if (ActualWidth == 0 || ActualHeight == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Still no actual size, using Dispatcher.BeginInvoke to delay...");
                    Dispatcher.BeginInvoke(new Action(() => StartMarqueeInternal()), DispatcherPriority.Loaded);
                    return;
                }
            }

            StartMarqueeInternal();
        }

        private void StartMarqueeInternal()
        {
//            System.Diagnostics.Debug.WriteLine($"=== StartMarqueeInternal Called ===");
//            System.Diagnostics.Debug.WriteLine($"Final ActualSize: {ActualWidth}x{ActualHeight}");

            // Update text properties
            _textBlock.Text = MarqueeText;
            _textBlock.Foreground = TextColor;
            _textBlock.FontFamily = TextFontFamily;
            _textBlock.FontSize = TextFontSize;

            // Force measure to get actual text dimensions
            _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = _textBlock.DesiredSize.Width;
            var textHeight = _textBlock.DesiredSize.Height;

 //           System.Diagnostics.Debug.WriteLine($"Text Size: {textWidth}x{textHeight}");

            // Use ActualWidth/ActualHeight if available, otherwise fall back to Width/Height
            var canvasWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 404);
            var canvasHeight = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 80);

//            System.Diagnostics.Debug.WriteLine($"Canvas Size: {canvasWidth}x{canvasHeight}");

            // Update canvas size to match control
            _canvas.Width = canvasWidth;
            _canvas.Height = canvasHeight;
            _canvas.Background = new SolidColorBrush(Colors.Transparent); // Ensure it has a background for hit testing

            // Force canvas layout
            _canvas.Measure(new Size(canvasWidth, canvasHeight));
            _canvas.Arrange(new Rect(0, 0, canvasWidth, canvasHeight));

            double startY = Position switch
            {
                MarqueePosition.Top or MarqueePosition.TopLeft or MarqueePosition.TopRight => 5,
                MarqueePosition.Center => Math.Max(5, (canvasHeight - textHeight) / 2),
                _ => Math.Max(5, canvasHeight - textHeight - 5) // Bottom positions with better margin
            };
            
            // Ensure text doesn't go below the canvas
            if (startY + textHeight > canvasHeight)
            {
                startY = Math.Max(0, canvasHeight - textHeight);
            }
            
            Canvas.SetTop(_textBlock, startY);
//            System.Diagnostics.Debug.WriteLine($"Text Y Position: {startY}");

            // Set initial position (start from right edge)
            double startX = canvasWidth;
            double endX = -textWidth;
            
            // Debug positioning
//            System.Diagnostics.Debug.WriteLine($"Canvas dimensions: {canvasWidth}x{canvasHeight}");
//            System.Diagnostics.Debug.WriteLine($"Text dimensions: {textWidth}x{textHeight}");
//            System.Diagnostics.Debug.WriteLine($"Control bounds: Left={Canvas.GetLeft(this)}, Top={Canvas.GetTop(this)}");
//            System.Diagnostics.Debug.WriteLine($"Parent container: {this.Parent?.GetType().Name}");
            
            Canvas.SetLeft(_textBlock, startX);
//            System.Diagnostics.Debug.WriteLine($"Text X Animation: {startX} -> {endX}");

            // Calculate animation duration based on speed
            var distance = startX - endX;
            var duration = TimeSpan.FromSeconds(distance / Speed);

            // Create the scroll animation
            var scrollAnimation = new DoubleAnimation
            {
                From = startX,
                To = endX,
                Duration = duration,
                EasingFunction = null
            };

            scrollAnimation.Completed += ScrollAnimation_Completed!;

            // Create and start the storyboard
            _scrollStoryboard = new Storyboard();
            _scrollStoryboard.Children.Add(scrollAnimation);
            Storyboard.SetTarget(scrollAnimation, _textBlock);
            Storyboard.SetTargetProperty(scrollAnimation, new PropertyPath("(Canvas.Left)"));

            _currentRepeatCount = 0;
            _maxRepeatCount = RepeatCount;
            _isAnimating = true;

//            System.Diagnostics.Debug.WriteLine($"Starting animation: Duration={duration.TotalSeconds}s, Speed={Speed}px/s");
//            System.Diagnostics.Debug.WriteLine($"TextBlock Visibility: {_textBlock.Visibility}");
//            System.Diagnostics.Debug.WriteLine($"Canvas Visibility: {_canvas.Visibility}");
//            System.Diagnostics.Debug.WriteLine($"Control Visibility: {this.Visibility}");

            // Try direct animation first, then storyboard as fallback
            try
            {
                // Direct animation approach
                _textBlock.BeginAnimation(Canvas.LeftProperty, scrollAnimation);
//                System.Diagnostics.Debug.WriteLine("Animation started successfully using BeginAnimation");
                
                // Add a test to verify animation is working by checking position after 1 second
                System.Windows.Threading.DispatcherTimer testTimer = new System.Windows.Threading.DispatcherTimer();
                testTimer.Interval = TimeSpan.FromSeconds(1);
                testTimer.Tick += (s, e) =>
                {
                    var currentLeft = Canvas.GetLeft(_textBlock);
//                    System.Diagnostics.Debug.WriteLine($"Animation check: TextBlock position after 1s = {currentLeft}");
                    testTimer.Stop();
                };
                testTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BeginAnimation failed: {ex.Message}, trying Storyboard...");
                // Fallback to storyboard
                _scrollStoryboard.Begin(_canvas, true);
//                System.Diagnostics.Debug.WriteLine("Animation started successfully using Storyboard");
            }
        }

        /// <summary>
        /// Stops the marquee animation or static display
        /// </summary>
        public void StopMarquee()
        {
            _isAnimating = false;
            _scrollStoryboard?.Stop();
            _repeatTimer?.Stop();
            _timeoutTimer?.Stop();
            _currentRepeatCount = 0;
        }

        /// <summary>
        /// Updates the marquee with new parameters and restarts if currently running
        /// </summary>
        public void UpdateMarquee(string text, Brush color, FontFamily fontFamily, double fontSize, 
            int repeatCount, MarqueePosition position, double speed, int displayDevice)
        {
            var wasAnimating = _isAnimating;
            
            if (wasAnimating)
                StopMarquee();

            MarqueeText = text;
            TextColor = color;
            TextFontFamily = fontFamily;
            TextFontSize = fontSize;
            RepeatCount = repeatCount;
            Position = position;
            Speed = speed;
            DesiredDisplayDevice = displayDevice;
            IsStaticMode = false;

            if (wasAnimating && !string.IsNullOrEmpty(text))
                StartMarquee();
        }

        /// <summary>
        /// Updates the control for static text display with countdown timer
        /// </summary>
        public void UpdateStaticText(string text, Brush color, FontFamily fontFamily, double fontSize,
            MarqueePosition position, int timeoutSeconds, int displayDevice)
        {
            StopMarquee(); // Stop any existing animation

            MarqueeText = text;
            TextColor = color;
            TextFontFamily = fontFamily;
            TextFontSize = fontSize;
            Position = position;
            TimeoutSeconds = timeoutSeconds;
            DesiredDisplayDevice = displayDevice;
            IsStaticMode = true;
        }

        /// <summary>
        /// Starts static text display with countdown timer
        /// </summary>
        public void StartStaticDisplay()
        {
            if (string.IsNullOrEmpty(MarqueeText))
            {
                System.Diagnostics.Debug.WriteLine("StartStaticDisplay aborted: Text is empty");
                return;
            }

//            System.Diagnostics.Debug.WriteLine($"=== StartStaticDisplay Called ===");
//            System.Diagnostics.Debug.WriteLine($"Text: '{MarqueeText}', Timeout: {TimeoutSeconds}s");

            // Force layout update if needed
            if (ActualWidth == 0 || ActualHeight == 0)
            {
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Arrange(new Rect(0, 0, Width > 0 ? Width : 404, Height > 0 ? Height : 80));
                UpdateLayout();
                
                if (ActualWidth == 0 || ActualHeight == 0)
                {
                    Dispatcher.BeginInvoke(new Action(() => StartStaticDisplayInternal()), DispatcherPriority.Loaded);
                    return;
                }
            }

            StartStaticDisplayInternal();
        }

        private void StartStaticDisplayInternal()
        {
//            System.Diagnostics.Debug.WriteLine($"=== StartStaticDisplayInternal Called ===");

            // Update text properties
            _textBlock.Text = MarqueeText;
            _textBlock.Foreground = TextColor;
            _textBlock.FontFamily = TextFontFamily;
            _textBlock.FontSize = TextFontSize;

            // Force measure to get actual text dimensions
            _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = _textBlock.DesiredSize.Width;
            var textHeight = _textBlock.DesiredSize.Height;

            var canvasWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 404);
            var canvasHeight = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 80);

            // Update canvas size
            _canvas.Width = canvasWidth;
            _canvas.Height = canvasHeight;
            _canvas.Background = new SolidColorBrush(Colors.Transparent);

            // Position text based on MarqueePosition
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
                _ => Math.Max(10, (canvasWidth - textWidth) / 2) // Center horizontally for other positions
            };

            // Ensure text fits within canvas bounds
            if (textY + textHeight > canvasHeight)
                textY = Math.Max(0, canvasHeight - textHeight);
            if (textX + textWidth > canvasWidth)
                textX = Math.Max(0, canvasWidth - textWidth);

            Canvas.SetLeft(_textBlock, textX);
            Canvas.SetTop(_textBlock, textY);

 //           System.Diagnostics.Debug.WriteLine($"Static text positioned at ({textX}, {textY})");

            // Set up timeout timer if specified
            if (TimeoutSeconds > 0)
            {
                _timeoutTimer = new DispatcherTimer();
                _timeoutTimer.Interval = TimeSpan.FromSeconds(TimeoutSeconds);
                _timeoutTimer.Tick += (sender, e) =>
                {
                    _timeoutTimer?.Stop();
//                    System.Diagnostics.Debug.WriteLine($"Static text timeout reached ({TimeoutSeconds}s)");
                    OnMarqueeCompleted();
                };
                _timeoutTimer.Start();
//                System.Diagnostics.Debug.WriteLine($"Timeout timer started for {TimeoutSeconds} seconds");
            }
            else
            {
 //               System.Diagnostics.Debug.WriteLine("No timeout set - text will display indefinitely");
            }

            _isAnimating = true; // Mark as active even though it's static
        }



        /// <summary>
        /// Sets the marquee to be clearly visible (for production use)
        /// </summary>
        public void EnsureVisible()
        {
            this.Visibility = Visibility.Visible;
            _canvas.Visibility = Visibility.Visible;
            _textBlock.Visibility = Visibility.Visible;
            this.Opacity = 1.0;
            
            // Ensure proper layering
            Panel.SetZIndex(this, 100);
            
            this.UpdateLayout();
        }

        private void ScrollAnimation_Completed(object sender, EventArgs e)
        {
            _currentRepeatCount++;

            if (_currentRepeatCount < _maxRepeatCount)
            {
                // Start the next repeat after a brief pause
                _repeatTimer.Interval = TimeSpan.FromMilliseconds(500);
                _repeatTimer.Start();
            }
            else
            {
                // Animation complete
                _isAnimating = false;
                OnMarqueeCompleted();
            }
        }

        private void RepeatTimer_Tick(object sender, EventArgs e)
        {
            _repeatTimer.Stop();
            
            if (_currentRepeatCount < _maxRepeatCount)
            {
                // Restart the animation
                var canvasWidth = ActualWidth > 0 ? ActualWidth : 800;
                var textWidth = _textBlock.DesiredSize.Width;
                
                double startX = canvasWidth;
                double endX = -textWidth;
                Canvas.SetLeft(_textBlock, startX);

                var distance = startX - endX;
                var duration = TimeSpan.FromSeconds(distance / Speed);

                var scrollAnimation = new DoubleAnimation
                {
                    From = startX,
                    To = endX,
                    Duration = duration,
                    EasingFunction = null
                };

                scrollAnimation.Completed += ScrollAnimation_Completed!;

                // Use direct animation for repeat as well
                try
                {
                    _textBlock.BeginAnimation(Canvas.LeftProperty, scrollAnimation);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Repeat BeginAnimation failed: {ex.Message}");
                    _scrollStoryboard = new Storyboard();
                    _scrollStoryboard.Children.Add(scrollAnimation);
                    Storyboard.SetTarget(scrollAnimation, _textBlock);
                    Storyboard.SetTargetProperty(scrollAnimation, new PropertyPath("(Canvas.Left)"));
                    _scrollStoryboard.Begin(_canvas, true);
                }
            }
        }

        /// <summary>
        /// Event raised when the marquee animation completes all repeats
        /// </summary>
        public event EventHandler? MarqueeCompleted;

        protected virtual void OnMarqueeCompleted()
        {
            MarqueeCompleted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            
            // If marquee is running, restart with new dimensions
            if (_isAnimating)
            {
                StopMarquee();
                StartMarquee();
            }
        }
    }
}