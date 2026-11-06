using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 文字超過「N 個中文字寬」時截斷並加上「…」(例如待播清單歌手最多 7 個中文字 + …)。
    /// values[0] = 文字、values[1] = FontFamily；ConverterParameter = N。
    /// 以字寬比例判斷 (與字級無關)，所以英數字較窄的名字可以放比 N 多的字元。
    /// </summary>
    public class TextWidthLimitConverter : IMultiValueConverter
    {
        private const double MeasureFontSize = 100;
        private const string Ellipsis = "…";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string text = values.Length > 0 ? values[0] as string ?? string.Empty : string.Empty;
            if (text.Length == 0) return text;
            if (!int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxChars) || maxChars <= 0)
                return text;
            var family = values.Length > 1 && values[1] is FontFamily f ? f : SystemFonts.MessageFontFamily;
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // 中文字寬 = 字級
            double limit = MeasureFontSize * maxChars + 0.5;
            if (Width(text, typeface) <= limit) return text;

            // 找出寬度不超過上限的最長前綴 (不切開代理字組)
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Width(text.Substring(0, mid), typeface) <= limit) lo = mid; else hi = mid - 1;
            }
            if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
            return text.Substring(0, lo).TrimEnd() + Ellipsis;
        }

        private static double Width(string text, Typeface typeface) =>
            new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, MeasureFontSize, Brushes.Black, 1.0)
                .WidthIncludingTrailingWhitespace;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
