using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Returns a blue brush when the bound bool is true (live/current quarter),
    /// and a dark-grey brush when false (archived quarter).
    /// Used in the ClassView history timeline to colour the ● / ○ dot icon.
    /// </summary>
    public class BoolToBlueConverter : IValueConverter
    {
        private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(25, 118, 210));   // #1976D2
        private static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(97,  97,  97));   // #616161

        static BoolToBlueConverter()
        {
            Blue.Freeze();
            Grey.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Blue : Grey;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException("BoolToBlueConverter is one-way only.");
    }
}
