using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Converter that returns Visible if value is not null, Collapsed if null
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported for NullToVisibilityConverter");
        }
    }
}