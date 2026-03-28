using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    /// <summary>
    /// One row in the student card list. Populated by ClassViewModel.BuildStudentCards().
    /// </summary>
    public class StudentCardRow
    {
        // ── Identity ──────────────────────────────────────────────
        public string SerialNumber { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string PhoneNumber { get; set; }
        public string Category { get; set; }

        // ── Fees ──────────────────────────────────────────────────
        public decimal QuarterlyFee { get; set; }
        public decimal PreviousPending { get; set; }
        public decimal TotalDue { get; set; }

        // ── Raw DataRowView reference (for edits / payment) ───────
        public System.Data.DataRowView SourceRow { get; set; }

        // ════════════════════════════════════════════════════════
        // COMPUTED VISUAL PROPERTIES
        // These are what the XAML bindings consume directly.
        // ════════════════════════════════════════════════════════

        // Status text + colours ───────────────────────────────────
        public string StatusText
        {
            get
            {
                if (TotalDue <= 0) return "✅ Paid";
                if (PreviousPending > 0) return "🔴 Overdue";
                return "🟡 Pending";
            }
        }

        public Brush StatusBackground
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0"));
            }
        }

        public Brush StatusBorderColor
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#81C784"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF9A9A"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB74D"));
            }
        }

        public Brush StatusForeground
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            }
        }

        // Left stripe colour ──────────────────────────────────────
        public Brush StripeColor
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));
            }
        }

        // Card border colour ──────────────────────────────────────
        public Brush CardBorderColor
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8E6C9"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCDD2"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0B2"));
            }
        }

        // Avatar circle ───────────────────────────────────────────
        public Brush AvatarBackground
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0"));
            }
        }

        public Brush AvatarForeground
        {
            get
            {
                if (TotalDue <= 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C"));
                if (PreviousPending > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            }
        }

        // Pending amount colour (column) ──────────────────────────
        public Brush PendingAmountColor =>
            PreviousPending > 0
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78909C"));

        public Brush TotalDueColor =>
            TotalDue > 0
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C"));

        // Category pill colours ───────────────────────────────────
        public Brush CategoryBackground
        {
            get
            {
                return (Category?.ToUpper()) switch
                {
                    "SC" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8EAF6")),
                    "OBC" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8E1")),
                    "ST" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F2F1")),
                    "GEN" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3E5F5")),
                    "GEN FW" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCE4EC")),
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5")),
                };
            }
        }

        public Brush CategoryForeground
        {
            get
            {
                return (Category?.ToUpper()) switch
                {
                    "SC" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#283593")),
                    "OBC" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F57F17")),
                    "ST" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#004D40")),
                    "GEN" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6A1B9A")),
                    "GEN FW" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#880E4F")),
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#424242")),
                };
            }
        }
    }
}