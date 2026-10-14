using System;
using System.Globalization;

namespace UltimateKtv
{
    /// <summary>
    /// Converts a string to a boolean. Returns true if the string is not null or empty, false otherwise.
    /// Used to disable buttons in the quick-word grid that are just placeholders.
    /// </summary>
    public class NotNullOrEmptyToBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !string.IsNullOrEmpty(value as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// Returns true if the value does NOT equal the parameter.
    /// Usage: Binding="{Binding Language, Converter={StaticResource NotEqualConverter}, ConverterParameter=國語}"
    /// </summary>
    public class NotEqualConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null && parameter == null) return false;
            if (value == null || parameter == null) return true;
            return !value.ToString()!.Equals(parameter.ToString(), StringComparison.Ordinal);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// 點歌人 → 顯示文字 (隨機播放/網路點歌等系統標記依介面語言顯示)。
    /// </summary>
    public class OrderedByDisplayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.OrderedBy(value as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// 歌曲語系 (資料庫的值) → 目前介面語言的顯示文字。只用於顯示，比對請繫結原始值。
    /// </summary>
    public class SongLanguageDisplayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.SongLangShort(value as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}