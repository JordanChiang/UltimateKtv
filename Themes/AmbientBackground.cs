using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace UltimateKtv
{
    public enum AmbientPlayback { Idle, Playing, Paused }

    /// <summary>
    /// 主畫面的動態背景 (themesettings.json 的 BackgroundEffect / GlassPanels)，放在主畫面版面最底層，不接收滑鼠。
    /// 流動光暈：幾團大型柔光緩慢漂移；隨狀態變化：切換分類換色、播放時呼吸、暫停時靜止變暗、開播時掃過一道光；
    /// 玻璃質感：暗角與細紋理 (面板半透明在 ThemeSettingsHandler.ApplyBackgroundSurfaces)。
    /// 效能：每團光暈各自 BitmapCache，動畫只改位移/縮放/透明度 (不重畫漸層)，並限制每秒 24 格；
    /// 只有換分類換色的約 1 秒內需要重畫光暈。
    /// </summary>
    public sealed class AmbientBackground : Grid
    {
        // 光暈移動很慢，24 格/秒已經平順，也讓背景只占用每秒 60 次畫面更新中的一部分
        private const int FrameRate = 24;
        private const double BlobSize = 1100;
        private const double SweepWidth = 900;
        private const double PausedOpacity = 0.3;
        private static readonly TimeSpan HueDuration = TimeSpan.FromSeconds(1.2);
        private static readonly Color DeepBlue = Color.FromRgb(0x3D, 0x4B, 0xD9);

        // 左側主選單各分類的光暈色 (key 同 ThemeSettings.NavMenuDefaultKeys)；不在表中的 (新進) 用主題強調色
        private static readonly Dictionary<string, Color> NavHues = new()
        {
            ["Rank"] = Color.FromRgb(0xFF, 0x6A, 0x3D),
            ["Singer"] = Color.FromRgb(0x9B, 0x7B, 0xFF),
            ["Fav"] = Color.FromRgb(0xFF, 0x5C, 0x9A),
            ["Num"] = Color.FromRgb(0x4D, 0x8B, 0xFF),
            ["Language"] = Color.FromRgb(0x5B, 0xD6, 0x8F),
            ["Input"] = Color.FromRgb(0x3F, 0xC1, 0xC9),
            ["Youtube"] = Color.FromRgb(0xFF, 0x44, 0x44)
        };

        private readonly Canvas _blobs = new();
        private readonly List<GradientStop> _hueStops = new();
        private readonly List<AnimationClock> _driftClocks = new();
        private Rectangle? _mainBlob;
        private double _mainBlobOpacity;
        private TranslateTransform? _sweepTransform;
        private Color _accent;
        private Color _hue;
        private bool _stateful;
        private AmbientPlayback _playback = AmbientPlayback.Idle;

        public AmbientBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        /// <summary>依主題設定建立背景圖層並開始漂移；只建立一次。</summary>
        public void Start(ThemeSettings settings, Color accent, Color light)
        {
            if (Children.Count > 0) return;

            _accent = _hue = accent;
            if (settings.BackgroundEffect > 0)
            {
                _stateful = settings.BackgroundEffect >= 2;
                Children.Add(_blobs);
                _mainBlobOpacity = 0.7;
                _mainBlob = AddBlob(accent, _mainBlobOpacity, -100, -300, 550, 300, 1.15, 22, tracksHue: true);
                AddBlob(DeepBlue, 0.65, 1120, 380, -650, -220, 0.9, 28, tracksHue: false);
                AddBlob(accent, 0.45, 650, 530, -350, -150, 1.2, 18, tracksHue: true);
                if (_stateful) Children.Add(CreateSweep(light));
            }
            if (settings.GlassPanels)
            {
                Children.Add(CreateVignette());
                Children.Add(CreateNoise());
            }
        }

        /// <summary>切換左側主選單分類時，光暈慢慢換成該分類的顏色。</summary>
        public void SetCategory(string? navKey)
        {
            if (!_stateful) return;

            var hue = navKey != null && NavHues.TryGetValue(navKey, out var c) ? c : _accent;
            if (hue == _hue) return;
            _hue = hue;

            foreach (var stop in _hueStops)
            {
                var anim = new ColorAnimation(WithAlpha(hue, stop.Color.A), HueDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                Timeline.SetDesiredFrameRate(anim, FrameRate);
                stop.BeginAnimation(GradientStop.ColorProperty, anim);
            }
        }

        /// <summary>播放中光暈呼吸、暫停時停止漂移並變暗、沒有播放時只漂移。</summary>
        public void SetPlayback(AmbientPlayback playback)
        {
            if (!_stateful || playback == _playback) return;
            _playback = playback;

            bool paused = playback == AmbientPlayback.Paused;
            foreach (var clock in _driftClocks)
            {
                if (paused) clock.Controller?.Pause();
                else clock.Controller?.Resume();
            }

            var fade = new DoubleAnimation(paused ? PausedOpacity : 1, TimeSpan.FromSeconds(0.8));
            Timeline.SetDesiredFrameRate(fade, FrameRate);
            _blobs.BeginAnimation(OpacityProperty, fade);

            if (_mainBlob == null) return;
            if (playback == AmbientPlayback.Playing)
            {
                var breathe = new DoubleAnimation(_mainBlobOpacity, 1.0, TimeSpan.FromSeconds(1.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                Timeline.SetDesiredFrameRate(breathe, FrameRate);
                _mainBlob.BeginAnimation(OpacityProperty, breathe);
            }
            else
            {
                var settle = new DoubleAnimation(_mainBlobOpacity, TimeSpan.FromSeconds(0.6));
                Timeline.SetDesiredFrameRate(settle, FrameRate);
                _mainBlob.BeginAnimation(OpacityProperty, settle);
            }
        }

        /// <summary>開播時由左到右掃過一道光。</summary>
        public void Sweep()
        {
            if (_sweepTransform == null) return;

            double width = ActualWidth > 0 ? ActualWidth : 1920;
            // 掃光移動快，不限制格數以免跳格；只持續約 1 秒
            var anim = new DoubleAnimation(-SweepWidth, width, TimeSpan.FromSeconds(1.1))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            _sweepTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private Rectangle AddBlob(Color color, double opacity, double x, double y,
            double driftX, double driftY, double driftScale, double seconds, bool tracksHue)
        {
            var stops = new[]
            {
                new GradientStop(WithAlpha(color, 255), 0.0),
                new GradientStop(WithAlpha(color, 140), 0.4),
                new GradientStop(WithAlpha(color, 0), 1.0)
            };
            var brush = new RadialGradientBrush();
            foreach (var stop in stops) brush.GradientStops.Add(stop);
            if (tracksHue) _hueStops.AddRange(stops);

            var scale = new ScaleTransform(1, 1, BlobSize / 2, BlobSize / 2);
            var translate = new TranslateTransform();
            var blob = new Rectangle
            {
                Width = BlobSize,
                Height = BlobSize,
                Fill = brush,
                Opacity = opacity,
                RenderTransform = new TransformGroup { Children = { scale, translate } },
                CacheMode = new BitmapCache { SnapsToDevicePixels = false }
            };
            Canvas.SetLeft(blob, x);
            Canvas.SetTop(blob, y);
            _blobs.Children.Add(blob);

            var duration = TimeSpan.FromSeconds(seconds);
            Drift(translate, TranslateTransform.XProperty, driftX, duration);
            Drift(translate, TranslateTransform.YProperty, driftY, duration);
            Drift(scale, ScaleTransform.ScaleXProperty, driftScale, duration);
            Drift(scale, ScaleTransform.ScaleYProperty, driftScale, duration);
            return blob;
        }

        private void Drift(Animatable target, DependencyProperty property, double to, TimeSpan duration)
        {
            var anim = new DoubleAnimation(to, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(anim, FrameRate);
            var clock = anim.CreateClock();
            target.ApplyAnimationClock(property, clock);
            _driftClocks.Add(clock);
        }

        private Rectangle CreateSweep(Color light)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.35),
                EndPoint = new Point(1, 0.65),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(WithAlpha(light, 0), 0.0),
                    new GradientStop(WithAlpha(light, 0x55), 0.5),
                    new GradientStop(WithAlpha(light, 0), 1.0)
                }
            };
            brush.Freeze();

            _sweepTransform = new TranslateTransform(-SweepWidth, 0);
            return new Rectangle
            {
                Width = SweepWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Fill = brush,
                RenderTransform = _sweepTransform,
                CacheMode = new BitmapCache { SnapsToDevicePixels = false }
            };
        }

        private static Rectangle CreateVignette()
        {
            var brush = new RadialGradientBrush
            {
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
                    new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1.0)
                }
            };
            brush.Freeze();
            return new Rectangle { Fill = brush, CacheMode = new BitmapCache() };
        }

        /// <summary>細微的灰階雜訊紋理，讓大片深色底不會顯得平板。</summary>
        private static Rectangle CreateNoise()
        {
            const int size = 160;
            var pixels = new byte[size * size * 4];
            var random = new Random(1);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte v = (byte)random.Next(256);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();

            var brush = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return new Rectangle { Fill = brush, Opacity = 0.05, CacheMode = new BitmapCache() };
        }

        private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    }
}
