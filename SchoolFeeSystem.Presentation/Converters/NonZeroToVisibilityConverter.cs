using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Returns Visible when the bound decimal/int value is greater than zero.
    /// Returns Collapsed otherwise.
    /// Used in the student card to show fee-component chips only when the
    /// amount is non-zero (e.g. Stationary, Red Cross Fund, Hostel, etc.)
    /// </summary>
    public class NonZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d) return d > 0m ? Visibility.Visible : Visibility.Collapsed;
            if (value is int    i)  return i > 0  ? Visibility.Visible : Visibility.Collapsed;
            if (value is double dbl)return dbl > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is float  f)  return f > 0f ? Visibility.Visible : Visibility.Collapsed;
            if (value is bool   b)  return b       ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException("NonZeroToVisibilityConverter is one-way only.");
    }
}
