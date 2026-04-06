using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Returns Collapsed when the bound string is null or empty, Visible otherwise.
    /// Used in ClassView to hide the OriginalFileAddedBadge and CurrentQuarterBadge
    /// TextBlocks when the VM has not yet populated them.
    /// </summary>
    public class NullOrEmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value?.ToString())
                ? Visibility.Collapsed
                : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException("NullOrEmptyToCollapsedConverter is one-way only.");
    }
}
