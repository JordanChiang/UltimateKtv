using System.Windows;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 歌曲列表列外觀風格 5~8 依主題色計算的筆刷。主程式 (ThemeSettingsHandler) 與 StyleSimulator 共用，
    /// 確保模擬畫面與實際畫面配色一致。回傳的筆刷皆已 Freeze。
    /// </summary>
    public static class RowStyleBrushes
    {
        /// <summary>列外觀風格選項總數 (1~Count)。</summary>
        public const int Count = 8;

        public static Dictionary<string, Brush> Create(Color primary, Color light)
        {
            var second = ShiftHue(primary, 50);
            var d = new Dictionary<string, Brush>
            {
                // 風格 5：霓虹底線 (無卡片的扁平清單，選取時底部粗霓虹線 + 由下往上的主色反光)
                ["BrushNeonUnderline"] = Horizontal((primary, 90, 0.0), (primary, 255, 0.15), (primary, 255, 0.85), (primary, 90, 1.0)),
                ["BrushNeonUnderlineHover"] = Horizontal((light, 70, 0.0), (light, 255, 0.15), (light, 255, 0.85), (light, 70, 1.0)),
                ["BrushNeonTint"] = Vertical((primary, 0, 0.0), (primary, 40, 0.5), (primary, 120, 1.0)),
                ["BrushNeonTintHover"] = Vertical((light, 0, 0.0), (light, 24, 0.5), (light, 80, 1.0)),

                // 風格 6：糖果光澤 (選取時整列填滿主色，上半部玻璃光澤)
                ["BrushGlossActive"] = Vertical((Scale(primary, 0.80), 255, 0.0), (Scale(primary, 0.55), 255, 0.5), (Scale(primary, 0.30), 255, 1.0)),
                ["BrushGlossHover"] = Vertical((Scale(light, 0.55), 200, 0.0), (Scale(light, 0.35), 200, 0.5), (Scale(light, 0.20), 200, 1.0)),

                // 風格 7：雙色漸層框 (主色 → 色相偏移的第二色，粗漸層外框 + 雙色淡底)
                ["BrushDuoRim"] = Horizontal((primary, 255, 0.0), (light, 255, 0.5), (second, 255, 1.0)),
                ["BrushDuoRimHover"] = Horizontal((light, 230, 0.0), (Colors.White, 230, 0.5), (Lighten(second), 230, 1.0)),
                ["BrushDuoTint"] = Horizontal((primary, 80, 0.0), (primary, 16, 0.5), (second, 80, 1.0)),
                ["BrushDuoTintHover"] = Horizontal((light, 48, 0.0), (light, 10, 0.5), (second, 48, 1.0)),

                // 風格 8：科技切角框 (四角 L 型括號 + 淡主色底)
                ["BrushBracketTint"] = Horizontal((primary, 60, 0.0), (primary, 22, 0.5), (primary, 60, 1.0)),
                ["BrushBracketTintHover"] = Horizontal((light, 40, 0.0), (light, 12, 0.5), (light, 40, 1.0)),
                ["BrushBracketBorder"] = Solid(primary, 110),
                ["BrushBracketBorderHover"] = Solid(light, 90),
            };
            return d;
        }

        private static Color Lighten(Color c)
            => Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2));

        /// <summary>保留亮度與飽和度，只旋轉色相 (度)。灰階顏色 (如冰川銀) 旋轉後不變。</summary>
        private static Color ShiftHue(Color c, double degrees)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, delta = max - min;
            if (delta < 1e-6) return c;
            double sat = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            double h = max == r ? (g - b) / delta + (g < b ? 6 : 0) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            h = ((h * 60 + degrees) % 360 + 360) % 360 / 360;

            double q = l < 0.5 ? l * (1 + sat) : l + sat - l * sat, p = 2 * l - q;
            double Channel(double t)
            {
                t = (t % 1 + 1) % 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            return Color.FromRgb((byte)Math.Round(Channel(h + 1.0 / 3) * 255), (byte)Math.Round(Channel(h) * 255), (byte)Math.Round(Channel(h - 1.0 / 3) * 255));
        }

        private static Color Scale(Color c, double f)
            => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

        private static SolidColorBrush Solid(Color c, byte a)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush Vertical(params (Color c, byte a, double offset)[] stops)
            => Gradient(new Point(0, 0), new Point(0, 1), stops);

        private static LinearGradientBrush Horizontal(params (Color c, byte a, double offset)[] stops)
            => Gradient(new Point(0, 0), new Point(1, 0), stops);

        private static LinearGradientBrush Gradient(Point start, Point end, (Color c, byte a, double offset)[] stops)
        {
            var collection = new GradientStopCollection();
            foreach (var (c, a, offset) in stops)
                collection.Add(new GradientStop(Color.FromArgb(a, c.R, c.G, c.B), offset));
            var brush = new LinearGradientBrush(collection, start, end);
            brush.Freeze();
            return brush;
        }
    }
}
