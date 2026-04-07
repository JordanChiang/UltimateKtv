using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// Converter that converts a boolean value to a Stretch enumeration value.
    /// True = Uniform (keep aspect ratio), False = Fill (stretch to fill)
    /// </summary>
    public class BoolToStretchConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // If true, keep aspect ratio (Uniform)
                // If false, stretch to fill (Fill)
                return boolValue ? Stretch.Uniform : Stretch.Fill;
            }
            
            // Default to Uniform if the value is not a boolean
            return Stretch.Uniform;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Stretch stretchValue)
            {
                // Convert back: Uniform = true, Fill = false
                return stretchValue == Stretch.Uniform;
            }
            
            // Default to true (keep aspect ratio)
            return true;
        }
    }
}