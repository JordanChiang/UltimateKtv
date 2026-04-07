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
}