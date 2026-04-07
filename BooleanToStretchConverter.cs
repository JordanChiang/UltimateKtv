using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// Converts a boolean value to a System.Windows.Media.Stretch value.
    /// This is typically used to bind a "Keep Aspect Ratio" setting (true/false)
    /// to a media player's Stretch property.
    /// - true  -> Stretch.Uniform (keeps aspect ratio)
    /// - false -> Stretch.Fill (stretches to fill)
    /// 
    /// This class implements MarkupExtension to be used as a singleton,
    /// which is a memory-efficient pattern for converters in WPF.
    /// </summary>
    public class BooleanToStretchConverter : MarkupExtension, IValueConverter
    {
        // The singleton instance. Declared as nullable ('?') to satisfy the C# compiler
        // since it's initialized on first use, not in a constructor. This resolves CS8618.
        private static BooleanToStretchConverter? _instance;

        /// <summary>
        /// Provides the singleton instance of the converter.
        /// </summary>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ??= new BooleanToStretchConverter();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Stretch.Uniform : Stretch.Fill;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This conversion is not needed for one-way bindings.
            throw new NotSupportedException();
        }
    }
}