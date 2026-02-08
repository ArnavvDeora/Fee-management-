using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Converter that returns Visible if value is true, Collapsed if false
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;

            return false;
        }
    }
}