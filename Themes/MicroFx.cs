using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UltimateKtv
{
    /// <summary>
    /// 微互動動畫 (themesettings.json 的 VisualEffects)：點歌時歌名飛到待播清單，
    /// 飛行期間清單維持點歌前的樣子，標籤抵達時新歌才出現 (插播時其他列往下滑一格) 並亮一下。
    /// 動畫都在 1 秒內結束，只動透明度與位移，不重建列。
    /// 主程式 (MainWindow.MicroFx.cs) 與 StyleSimulator 共用 (StyleSimulator 以連結原始檔編譯，不可引用只存在主程式的類別)。
    /// </summary>
    public static class MicroFx
    {
        public static readonly TimeSpan FlyDuration = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan RowFadeIn = TimeSpan.FromMilliseconds(220);
        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan CancelFade = TimeSpan.FromMilliseconds(150);

        /// <summary>FlyTo 的標籤與閃光；目標位置已不是這首歌時 (清單又變了) 用 Cancel 提早結束。</summary>
        public sealed class Flight
        {
            private readonly Canvas _layer;
            private readonly UIElement[] _elements;

            internal Flight(Canvas layer, params UIElement[] elements)
            {
                _layer = layer;
                _elements = elements;
            }

            /// <summary>標籤與閃光從目前的透明度淡出後移除 (已結束的不動)。</summary>
            public void Cancel()
            {
                foreach (var element in _elements)
                {
                    if (!_layer.Children.Contains(element)) continue;
                    var fade = new DoubleAnimation(0, CancelFade);
                    fade.Completed += (_, _) => _layer.Children.Remove(element);
                    element.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
        }

        /// <summary>歌名標籤從 from 以弧線飛到 target (都是 layer 座標)，抵達時 target 用主題色亮一下。</summary>
        public static Flight FlyTo(Canvas layer, string text, Point from, Rect target)
        {
            var label = FlyLabel(layer, text, from, target);
            var flash = Flash(layer, target, FlyDuration);
            return new Flight(layer, label, flash);
        }

        /// <summary>
        /// 點歌：清單內容已經換好，新歌那一列 (原本是空列) 先維持空白，等標籤抵達再淡入。
        /// 只隱藏新加入的那一列，其他列照常顯示。
        /// </summary>
        public static void RevealAfterFly(UIElement row)
        {
            row.BeginAnimation(UIElement.OpacityProperty, HoldThenEase(0, 1, FlyDuration, RowFadeIn));
        }

        /// <summary>
        /// 插播：清單內容已經換好 (新歌在第 1 列)。標籤飛行期間第 2 列之後往上錯一格，看起來仍是插播前的清單；
        /// 標籤抵達時再一起往下滑一格，第 1 列同時淡入。
        /// rows 只傳插播後位置真的往後移的列 (第 1 列到這首歌原本的位置)；更後面的列沒有移動，不要傳。
        /// </summary>
        public static void SlideDownAfterFly(IReadOnlyList<UIElement> rows, double rowHeight)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (i == 0)
                {
                    row.BeginAnimation(UIElement.OpacityProperty, HoldThenEase(0, 1, FlyDuration, SlideDuration));
                }
                RowTranslate(row)?.BeginAnimation(TranslateTransform.YProperty,
                    HoldThenEase(i == 0 ? -rowHeight / 2 : -rowHeight, 0, FlyDuration, SlideDuration));
            }
        }

        /// <summary>
        /// 立即結束 RevealAfterFly / SlideDownAfterFly 在這一列的效果，回到原本的透明度與位置。
        /// 這些效果是套在固定的列上，飛行期間這一列換成別首歌 (插播、切歌、刪歌、換頁) 就對不上，要取消。
        /// </summary>
        public static void StopRowEffects(UIElement row)
        {
            row.BeginAnimation(UIElement.OpacityProperty, null);
            if (row.RenderTransform is TranslateTransform t && !t.IsFrozen)
            {
                t.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }

        /// <summary>元素在 layer 座標中的範圍。</summary>
        public static Rect Bounds(FrameworkElement element, Visual layer) =>
            element.TransformToVisual(layer).TransformBounds(new Rect(element.RenderSize));

        private static UIElement FlyLabel(Canvas layer, string text, Point from, Rect target)
        {
            // YouTube 影片標題常很長，限制寬度，超出以 … 截斷
            var textBlock = new TextBlock { Text = text, FontSize = 30, FontWeight = FontWeights.Bold, MaxWidth = 560, TextTrimming = TextTrimming.CharacterEllipsis };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "BrushOnAccentActive");
            var label = new Border
            {
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(20, 6, 20, 6),
                IsHitTestVisible = false,
                Child = textBlock
            };
            label.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = label.DesiredSize;

            var translate = new TranslateTransform();
            var scale = new ScaleTransform(1, 1, size.Width / 2, size.Height / 2);
            label.RenderTransform = new TransformGroup { Children = { scale, translate } };
            layer.Children.Add(label);

            // X 走緩進緩出、Y 先微微反向再落下，合起來是一條弧線
            var flyX = new DoubleAnimation(from.X - size.Width / 2, target.Left + 8, FlyDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            var flyY = new DoubleAnimation(from.Y - size.Height / 2, target.Top + (target.Height - size.Height) / 2, FlyDuration)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.4 }
            };
            var shrink = new DoubleAnimation(1, 0.85, FlyDuration);
            var fadeOut = HoldThenEase(1, 0, FlyDuration - TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) => layer.Children.Remove(label);

            translate.BeginAnimation(TranslateTransform.XProperty, flyX);
            translate.BeginAnimation(TranslateTransform.YProperty, flyY);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            label.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            return label;
        }

        private static UIElement Flash(Canvas layer, Rect target, TimeSpan delay)
        {
            var flash = new Border
            {
                Width = target.Width,
                Height = target.Height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                Opacity = 0
            };
            flash.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush");
            flash.SetResourceReference(Border.BackgroundProperty, "BrushAccentDim");
            Canvas.SetLeft(flash, target.Left);
            Canvas.SetTop(flash, target.Top);
            layer.Children.Add(flash);

            var anim = new DoubleAnimationUsingKeyFrames { Duration = delay + FlashDuration };
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + FlashDuration),
                new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            anim.Completed += (_, _) => layer.Children.Remove(flash);
            flash.BeginAnimation(UIElement.OpacityProperty, anim);
            return flash;
        }

        /// <summary>
        /// 先停在 from 直到 hold，再以 duration 緩出到 to；結束後放掉動畫 (屬性回到原值，原值即 to)。
        /// 用關鍵影格而不是 BeginTime：BeginTime 等待期間會顯示原值，列會先閃一下再消失。
        /// </summary>
        private static DoubleAnimationUsingKeyFrames HoldThenEase(double from, double to, TimeSpan hold, TimeSpan duration)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = hold + duration, FillBehavior = FillBehavior.Stop };
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(hold)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(hold + duration),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            return anim;
        }

        /// <summary>取得 (必要時建立) 列的位移 transform；列已有其他 transform 時不動它。</summary>
        private static TranslateTransform? RowTranslate(UIElement row)
        {
            if (row.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
            if (row.RenderTransform != null && row.RenderTransform != Transform.Identity) return null;
            var translate = new TranslateTransform();
            row.RenderTransform = translate;
            return translate;
        }
    }
}
