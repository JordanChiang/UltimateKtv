using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// A custom FrameworkElement that renders text with an outline/stroke and fill.
    /// </summary>
    public class OutlinedTextBlock : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(new FontFamily("Microsoft JhengHei"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontStyleProperty =
            DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public OutlinedTextBlock()
        {
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public FontWeight FontWeight
        {
            get => (FontWeight)GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public FontStyle FontStyle
        {
            get => (FontStyle)GetValue(FontStyleProperty);
            set => SetValue(FontStyleProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        private FormattedText CreateFormattedText()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
            return new FormattedText(
                string.IsNullOrEmpty(Text) ? " " : Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                Math.Max(1.0, FontSize),
                Fill ?? Brushes.White,
                dpi.PixelsPerDip
            );
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new Size(0, 0);
            }

            var formattedText = CreateFormattedText();
            double extra = StrokeThickness;
            return new Size(formattedText.WidthIncludingTrailingWhitespace + extra * 2, formattedText.Height + extra * 2);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            var formattedText = CreateFormattedText();
            double offset = StrokeThickness > 0 ? StrokeThickness : 0;
            var origin = new Point(offset, offset);

            if (StrokeThickness > 0 && Stroke != null)
            {
                var geometry = formattedText.BuildGeometry(origin);
                if (geometry != null)
                {
                    // Draw outer stroke border behind the text (thickness * 2 because half is covered by the fill)
                    var pen = new Pen(Stroke, StrokeThickness * 2)
                    {
                        LineJoin = PenLineJoin.Round,
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    pen.Freeze();
                    drawingContext.DrawGeometry(null, pen, geometry);

                    // Draw crisp solid text fill on top of the stroke
                    drawingContext.DrawGeometry(Fill ?? Brushes.White, null, geometry);
                }
            }
            else
            {
                drawingContext.DrawText(formattedText, origin);
            }
        }
    }
}
