using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.Converters
{
    /// <summary>
    /// 3-TIER COLOR SYSTEM for Fee Collection
    /// RED = Previous Quarter Pending (Priority!)
    /// YELLOW = Only Current Quarter Due
    /// WHITE = No Fees Pending
    /// </summary>
    public class PendingFeesToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Default to white (no highlighting)
            if (value == null || !(value is DataRowView rowView))
            {
                return Brushes.White;
            }

            try
            {
                var table = rowView.Row.Table;

                // ============================================
                // FIND COLUMNS (case-insensitive, flexible)
                // ============================================

                // Find "Previous Quarter Pending" or similar
                var previousCol = table.Columns
                    .Cast<DataColumn>()
                    .FirstOrDefault(c =>
                        c.ColumnName.ToLower().Contains("previous") ||
                        (c.ColumnName.ToLower().Contains("pending") &&
                         !c.ColumnName.ToLower().Contains("quarterly") &&
                         !c.ColumnName.ToLower().Contains("current")));

                // Find "Quarterly Fees" or "Current Quarter"
                var currentCol = table.Columns
                    .Cast<DataColumn>()
                    .FirstOrDefault(c =>
                        c.ColumnName.ToLower().Contains("quarterly") ||
                        c.ColumnName.ToLower().Contains("current quarter") ||
                        (c.ColumnName.ToLower().Contains("current") &&
                         c.ColumnName.ToLower().Contains("fee")));

                // ============================================
                // EXTRACT VALUES SAFELY
                // ============================================

                decimal previousAmount = 0;
                decimal currentAmount = 0;

                if (previousCol != null)
                {
                    try
                    {
                        string raw = rowView[previousCol.ColumnName]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            // Remove currency symbols and parse
                            raw = raw.Replace("₹", "").Replace(",", "").Trim();
                            decimal.TryParse(raw, out previousAmount);
                        }
                    }
                    catch
                    {
                        // Ignore parse errors, keep as 0
                    }
                }

                if (currentCol != null)
                {
                    try
                    {
                        string raw = rowView[currentCol.ColumnName]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            // Remove currency symbols and parse
                            raw = raw.Replace("₹", "").Replace(",", "").Trim();
                            decimal.TryParse(raw, out currentAmount);
                        }
                    }
                    catch
                    {
                        // Ignore parse errors, keep as 0
                    }
                }

                // ============================================
                // APPLY 3-TIER COLOR SYSTEM
                // ============================================

                if (previousAmount > 0)
                {
                    // 🔴 RED: Has old pending fees - PRIORITY!
                    return new SolidColorBrush(Color.FromRgb(255, 205, 210)); // #FFCDD2
                }
                else if (currentAmount > 0)
                {
                    // 🟡 YELLOW: Only current quarter due
                    return new SolidColorBrush(Color.FromRgb(255, 249, 196)); // #FFF9C4
                }
                else
                {
                    // ⚪ WHITE: No fees pending - All clear!
                    return Brushes.White;
                }
            }
            catch (Exception)
            {
                // ✅ SAFETY: If anything goes wrong, return white (no highlighting)
                // This prevents crashes from null columns or unexpected data
                return Brushes.White;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This converter only works one-way (from data → color)
            throw new NotImplementedException("PendingFeesToBrushConverter only supports one-way conversion");
        }
    }
}