using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    /// <summary>
    /// UI model for one student row in the FeeCollectionView card list.
    /// Inherits ObservableObject so IsSelected updates the card highlight.
    /// </summary>
    public partial class FeeStudentCard : ObservableObject
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string SerialNumber { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string PhoneNumber { get; set; }
        public string Category { get; set; }

        // ── Fee amounts ───────────────────────────────────────────────────────
        public decimal QuarterlyFee { get; set; }
        public decimal PreviousPending { get; set; }
        public decimal TotalDue { get; set; }

        // ── Source reference for edits ─────────────────────────────────────
        public System.Data.DataRowView SourceRow { get; set; }

        // ── Selection state (bound to card highlight style) ───────────────
        [ObservableProperty]
        private bool isSelected;

        // ══════════════════════════════════════════════════════════════════════
        // COMPUTED VISUAL PROPERTIES  (same colour logic as StudentCardRow)
        // ══════════════════════════════════════════════════════════════════════

        private static Brush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        // Status ──────────────────────────────────────────────────────────────
        public string StatusText =>
            TotalDue <= 0 ? "✅ Paid" :
            PreviousPending > 0 ? "🔴 Overdue" : "🟡 Pending";

        public Brush StatusBackground =>
            TotalDue <= 0 ? Brush("#E8F5E9") :
            PreviousPending > 0 ? Brush("#FFEBEE") : Brush("#FFF3E0");

        public Brush StatusBorderColor =>
            TotalDue <= 0 ? Brush("#81C784") :
            PreviousPending > 0 ? Brush("#EF9A9A") : Brush("#FFB74D");

        public Brush StatusForeground =>
            TotalDue <= 0 ? Brush("#2E7D32") :
            PreviousPending > 0 ? Brush("#C62828") : Brush("#E65100");

        // Left stripe ─────────────────────────────────────────────────────────
        public Brush StripeColor =>
            TotalDue <= 0 ? Brush("#4CAF50") :
            PreviousPending > 0 ? Brush("#F44336") : Brush("#FF9800");

        // Card border ─────────────────────────────────────────────────────────
        public Brush CardBorderColor =>
            TotalDue <= 0 ? Brush("#C8E6C9") :
            PreviousPending > 0 ? Brush("#FFCDD2") : Brush("#FFE0B2");

        // Avatar circle ───────────────────────────────────────────────────────
        public Brush AvatarBackground =>
            TotalDue <= 0 ? Brush("#E8F5E9") :
            PreviousPending > 0 ? Brush("#FFEBEE") : Brush("#FFF3E0");

        public Brush AvatarForeground =>
            TotalDue <= 0 ? Brush("#388E3C") :
            PreviousPending > 0 ? Brush("#C62828") : Brush("#E65100");

        // Amount text colours ─────────────────────────────────────────────────
        public Brush PendingAmountColor =>
            PreviousPending > 0 ? Brush("#C62828") : Brush("#78909C");

        public Brush TotalDueColor =>
            TotalDue > 0 ? Brush("#D32F2F") : Brush("#388E3C");

        // Category pill ───────────────────────────────────────────────────────
        public Brush CategoryBackground =>
            (Category?.ToUpper()) switch
            {
                "SC" => Brush("#E8EAF6"),
                "OBC" => Brush("#FFF8E1"),
                "ST" => Brush("#E0F2F1"),
                "GEN" => Brush("#F3E5F5"),
                "GEN FW" => Brush("#FCE4EC"),
                "FW BC" => Brush("#FCE4EC"),
                "BC" => Brush("#E8F5E9"),
                _ => Brush("#F5F5F5"),
            };

        public Brush CategoryForeground =>
            (Category?.ToUpper()) switch
            {
                "SC" => Brush("#283593"),
                "OBC" => Brush("#F57F17"),
                "ST" => Brush("#004D40"),
                "GEN" => Brush("#6A1B9A"),
                "GEN FW" => Brush("#880E4F"),
                "FW BC" => Brush("#880E4F"),
                "BC" => Brush("#1B5E20"),
                _ => Brush("#424242"),
            };
    }
}