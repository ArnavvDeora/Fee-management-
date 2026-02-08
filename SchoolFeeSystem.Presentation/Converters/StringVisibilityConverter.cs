using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Converts string comparison to Visibility
    /// Shows element if string matches parameter
    /// </summary>
    public class StringVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            string stringValue = value.ToString();
            string compareValue = parameter.ToString();

            return string.Equals(stringValue, compareValue, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}