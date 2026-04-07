// DecimalToBoolConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    public class DecimalToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is decimal d && d > 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}