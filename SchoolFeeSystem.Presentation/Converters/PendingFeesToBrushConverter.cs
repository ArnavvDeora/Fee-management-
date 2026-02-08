using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// Converter that returns Red brush if student has pending fees, otherwise White
    /// </summary>
    public class PendingFeesToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataRowView rowView)
            {
                var table = rowView.Row.Table;

                var pendingColumns = table.Columns
                    .Cast<DataColumn>()
                    .Where(c => c.ColumnName.ToLower().Contains("pending") ||
                               c.ColumnName.ToLower().Contains("previous") ||
                               c.ColumnName.ToLower().Contains("due"))
                    .ToList();

                foreach (var col in pendingColumns)
                {
                    string raw = rowView[col.ColumnName]?.ToString()?.Trim();
                    if (decimal.TryParse(raw, out decimal amount) && amount > 0)
                    {
                        return new SolidColorBrush(Color.FromRgb(255, 200, 200)); // Light red
                    }
                }
            }

            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}