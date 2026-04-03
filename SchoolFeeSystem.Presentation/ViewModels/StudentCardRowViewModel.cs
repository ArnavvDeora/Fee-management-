using System.Data;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    /// <summary>
    /// One row in the student card list. Populated by ClassViewModel.BuildStudentCards().
    /// SINGLE definition — the duplicate in ClassViewModel.cs must be deleted.
    /// </summary>
    public class StudentCardRow
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

        // ── Fee components (each shown as a chip when > 0) ────────────────────
        public decimal Stationary { get; set; }
        public decimal DevelopmentWelfare { get; set; }
        public decimal StudentActivities { get; set; }
        public decimal InstitutionalSecurity { get; set; }
        public decimal ComprehensiveInsurance { get; set; }
        public decimal RedCrossFund { get; set; }
        public decimal Hostel { get; set; }

        // ── Source row reference (for FeeCollection navigation) ───────────────
        public DataRowView SourceRow { get; set; }

        // ── Bool visibility helpers (bind with BoolToVisibilityConverter) ─────
        public bool HasStationary => Stationary > 0;
        public bool HasWelfare => DevelopmentWelfare > 0;
        public bool HasStudentActivities => StudentActivities > 0;
        public bool HasInstitutional => InstitutionalSecurity > 0;
        public bool HasInsurance => ComprehensiveInsurance > 0;
        public bool HasRedCross => RedCrossFund > 0;
        public bool HasHostel => Hostel > 0;
        public bool HasPreviousPending => PreviousPending > 0;

        // ── Left colour stripe ────────────────────────────────────────────────
        public string StripeColor =>
            PreviousPending > 0 ? "#E53935" :
            QuarterlyFee > 0 ? "#FB8C00" :
                                   "#43A047";

        // ── Status badge ──────────────────────────────────────────────────────
        public string StatusText =>
            TotalDue > 0 ? "⚠ Pending" : "✔ Paid";

        public string StatusBackground =>
            TotalDue > 0 ? "#FFF3E0" : "#E8F5E9";

        public string StatusForeground =>
            TotalDue > 0 ? "#E65100" : "#2E7D32";

        public string StatusBorderColor =>
            TotalDue > 0 ? "#FFCC02" : "#66BB6A";

        // ── Avatar ────────────────────────────────────────────────────────────
        public string AvatarBackground =>
            PreviousPending > 0 ? "#FFCDD2" :
            QuarterlyFee > 0 ? "#FFF9C4" :
                                   "#E8F5E9";

        public string AvatarForeground =>
            PreviousPending > 0 ? "#C62828" :
            QuarterlyFee > 0 ? "#F57F17" :
                                   "#2E7D32";

        // ── Card border ───────────────────────────────────────────────────────
        public string CardBorderColor =>
            PreviousPending > 0 ? "#EF9A9A" :
            QuarterlyFee > 0 ? "#FFE082" :
                                   "#A5D6A7";

        // ── Fee amount colours ────────────────────────────────────────────────
        public string PendingAmountColor =>
            PreviousPending > 0 ? "#C62828" : "#757575";

        public string TotalDueColor =>
            TotalDue > 0 ? "#E53935" : "#2E7D32";

        // ── Category pill colours ─────────────────────────────────────────────
        public string CategoryBackground => Category?.ToUpper() switch
        {
            "SC" => "#E3F2FD",
            "ST" => "#E8EAF6",
            "OBC" => "#FFF8E1",
            "GEN" => "#E8F5E9",
            "BC" => "#F3E5F5",
            "GEN FW" => "#E0F2F1",
            "FW BC" => "#FCE4EC",
            "OBC FW" => "#FFF3E0",
            _ => "#F5F5F5"
        };

        public string CategoryForeground => Category?.ToUpper() switch
        {
            "SC" => "#1565C0",
            "ST" => "#283593",
            "OBC" => "#F57F17",
            "GEN" => "#2E7D32",
            "BC" => "#6A1B9A",
            "GEN FW" => "#00695C",
            "FW BC" => "#880E4F",
            "OBC FW" => "#E65100",
            _ => "#424242"
        };
    }
}