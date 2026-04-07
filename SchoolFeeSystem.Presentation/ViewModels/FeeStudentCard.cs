using CommunityToolkit.Mvvm.ComponentModel;
using System.Data;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ════════════════════════════════════════════════════════════════════════
    // FeeStudentCard  — one card in the scrollable student list (Fee Collection)
    //
    // Kept in a dedicated file so it never collides with StudentCardRow
    // (used by ClassViewModel / Dashboard).  Both live in the same namespace
    // but have distinct, unambiguous names.
    //
    // FIX: All computed / colour properties that XAML may bind with TwoWay or
    // OneWayToSource now have a private setter.  WPF's binding engine requires
    // a setter even when the binding is effectively read-only from the UI side.
    // The computed values are still derived from ScholarshipPct / TotalDue /
    // PreviousPending — their setters just call OnPropertyChanged for every
    // dependent property so the UI stays in sync.
    // ════════════════════════════════════════════════════════════════════════
    public partial class FeeStudentCard : ObservableObject
    {
        // ── Identity ──────────────────────────────────────────────────────

        public string SerialNumber { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string PhoneNumber { get; set; }
        public string Category { get; set; }

        // ── Raw quarterly fee (BEFORE scholarship) ────────────────────────

        private decimal _quarterlyFeeRaw;
        public decimal QuarterlyFeeRaw
        {
            get => _quarterlyFeeRaw;
            set
            {
                if (SetProperty(ref _quarterlyFeeRaw, value))
                    RefreshFeeProperties();
            }
        }

        // ── Scholarship percentage (e.g. 10 = 10 %) ──────────────────────

        private decimal _scholarshipPct;
        public decimal ScholarshipPct
        {
            get => _scholarshipPct;
            set
            {
                if (SetProperty(ref _scholarshipPct, value))
                    RefreshFeeProperties();
            }
        }

        // ── Computed fee values — private set so TwoWay bindings don't crash

        private decimal _quarterlyFee;
        public decimal QuarterlyFee
        {
            get => _quarterlyFee;
            private set => SetProperty(ref _quarterlyFee, value);
        }

        private decimal _scholarshipDiscount;
        public decimal ScholarshipDiscount
        {
            get => _scholarshipDiscount;
            private set => SetProperty(ref _scholarshipDiscount, value);
        }

        // ── Display helpers ───────────────────────────────────────────────

        private string _scholarshipDisplay;
        public string ScholarshipDisplay
        {
            get => _scholarshipDisplay;
            private set => SetProperty(ref _scholarshipDisplay, value);
        }

        private bool _hasScholarship;
        public bool HasScholarship
        {
            get => _hasScholarship;
            private set => SetProperty(ref _hasScholarship, value);
        }

        // ── Pending / Due ─────────────────────────────────────────────────

        private decimal _previousPending;
        public decimal PreviousPending
        {
            get => _previousPending;
            set
            {
                if (SetProperty(ref _previousPending, value))
                    RefreshStatusProperties();
            }
        }

        private decimal _totalDue;
        /// <summary>Set by RebuildCards; = QuarterlyFee + PreviousPending</summary>
        public decimal TotalDue
        {
            get => _totalDue;
            set
            {
                if (SetProperty(ref _totalDue, value))
                    RefreshStatusProperties();
            }
        }

        // ── Selection ─────────────────────────────────────────────────────

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public DataRowView SourceRow { get; set; }

        // ── Colour / status properties — private set for TwoWay safety ────

        private string _stripeColor;
        public string StripeColor { get => _stripeColor; private set => SetProperty(ref _stripeColor, value); }

        private string _avatarBackground;
        public string AvatarBackground { get => _avatarBackground; private set => SetProperty(ref _avatarBackground, value); }

        private string _avatarForeground;
        public string AvatarForeground { get => _avatarForeground; private set => SetProperty(ref _avatarForeground, value); }

        private string _cardBorderColor;
        public string CardBorderColor { get => _cardBorderColor; private set => SetProperty(ref _cardBorderColor, value); }

        private string _pendingAmountColor;
        public string PendingAmountColor { get => _pendingAmountColor; private set => SetProperty(ref _pendingAmountColor, value); }

        private string _totalDueColor;
        public string TotalDueColor { get => _totalDueColor; private set => SetProperty(ref _totalDueColor, value); }

        private string _statusText;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private string _statusBackground;
        public string StatusBackground { get => _statusBackground; private set => SetProperty(ref _statusBackground, value); }

        private string _statusForeground;
        public string StatusForeground { get => _statusForeground; private set => SetProperty(ref _statusForeground, value); }

        private string _statusBorderColor;
        public string StatusBorderColor { get => _statusBorderColor; private set => SetProperty(ref _statusBorderColor, value); }

        private string _categoryBackground;
        public string CategoryBackground { get => _categoryBackground; private set => SetProperty(ref _categoryBackground, value); }

        private string _categoryForeground;
        public string CategoryForeground { get => _categoryForeground; private set => SetProperty(ref _categoryForeground, value); }

        // ── Constructor — seed computed values ────────────────────────────

        public FeeStudentCard()
        {
            RefreshFeeProperties();
            RefreshStatusProperties();
        }

        // ── Private helpers ───────────────────────────────────────────────

        /// <summary>
        /// Recomputes all properties that derive from ScholarshipPct or QuarterlyFeeRaw.
        /// Called whenever either of those changes.
        /// </summary>
        private void RefreshFeeProperties()
        {
            QuarterlyFee = _scholarshipPct > 0
                                    ? _quarterlyFeeRaw * (1m - _scholarshipPct / 100m)
                                    : _quarterlyFeeRaw;

            ScholarshipDiscount = _quarterlyFeeRaw - QuarterlyFee;
            ScholarshipDisplay = _scholarshipPct > 0 ? $"{_scholarshipPct:N0}%" : "";
            HasScholarship = _scholarshipPct > 0;
        }

        /// <summary>
        /// Recomputes all colour / status properties that derive from TotalDue or PreviousPending.
        /// Called whenever either of those changes.
        /// </summary>
        private void RefreshStatusProperties()
        {
            StripeColor = _totalDue <= 0 ? "#4CAF50"
                              : _previousPending > 0 ? "#F44336"
                              : "#FF9800";

            AvatarBackground = _totalDue <= 0 ? "#E8F5E9"
                              : _previousPending > 0 ? "#FFEBEE"
                              : "#FFF3E0";

            AvatarForeground = _totalDue <= 0 ? "#2E7D32"
                              : _previousPending > 0 ? "#C62828"
                              : "#E65100";

            CardBorderColor = _totalDue <= 0 ? "#C8E6C9"
                              : _previousPending > 0 ? "#FFCDD2"
                              : "#FFE0B2";

            PendingAmountColor = _previousPending > 0 ? "#C62828" : "#546E7A";

            TotalDueColor = _totalDue <= 0 ? "#2E7D32"
                              : _totalDue > 10000 ? "#C62828"
                              : "#E65100";

            StatusText = _totalDue <= 0 ? "✅ Paid"
                              : _previousPending > 0 ? "🔴 Overdue"
                              : "⏳ Pending";

            StatusBackground = _totalDue <= 0 ? "#E8F5E9"
                              : _previousPending > 0 ? "#FFEBEE"
                              : "#FFF8E1";

            StatusForeground = _totalDue <= 0 ? "#2E7D32"
                              : _previousPending > 0 ? "#C62828"
                              : "#E65100";

            StatusBorderColor = _totalDue <= 0 ? "#A5D6A7"
                              : _previousPending > 0 ? "#EF9A9A"
                              : "#FFE082";

            RefreshCategoryColors();
        }

        private void RefreshCategoryColors()
        {
            CategoryBackground = Category?.ToUpper() switch
            {
                "SC" => "#E3F2FD",
                "ST" => "#E8EAF6",
                "OBC" => "#FFF8E1",
                "GEN" or "GENERAL" => "#E8F5E9",
                "BC" => "#F3E5F5",
                "GEN FW" => "#E0F2F1",
                "FW BC" => "#FCE4EC",
                "OBC FW" => "#FFF3E0",
                _ => "#F5F5F5"
            };

            CategoryForeground = Category?.ToUpper() switch
            {
                "SC" => "#1565C0",
                "ST" => "#283593",
                "OBC" => "#F57F17",
                "GEN" or "GENERAL" => "#2E7D32",
                "BC" => "#6A1B9A",
                "GEN FW" => "#00695C",
                "FW BC" => "#880E4F",
                "OBC FW" => "#E65100",
                _ => "#424242"
            };
        }
    }
}