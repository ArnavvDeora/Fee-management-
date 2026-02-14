using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Company Gate Pass - 2-hour monthly allowance for minor time adjustments
    /// Key Rules:
    /// 1. Always consumed first before personal leave allowance
    /// 2. Maximum 2 uses per month (regardless of total time used)
    /// 3. Resets on the 1st of each month
    /// </summary>
    public class CompanyGatePass
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }

        /// <summary>
        /// Month for which this gate pass applies (1-12)
        /// </summary>
        public int Month { get; set; }

        /// <summary>
        /// Year for which this gate pass applies
        /// </summary>
        public int Year { get; set; }

        /// <summary>
        /// Total allowance in minutes (default: 120 minutes = 2 hours)
        /// </summary>
        public int TotalAllowanceMinutes { get; set; } = 120;

        /// <summary>
        /// Minutes already used from this gate pass
        /// </summary>
        public int UsedMinutes { get; set; } = 0;

        /// <summary>
        /// Number of times the gate pass has been used
        /// Maximum: 2 uses per month
        /// </summary>
        public int TimesUsed { get; set; } = 0;

        /// <summary>
        /// Maximum uses allowed per month (default: 2)
        /// </summary>
        public int MaxUsesPerMonth { get; set; } = 2;

        /// <summary>
        /// When this record was created
        /// </summary>
        public DateTime CreatedOn { get; set; } = DateTime.Now;

        /// <summary>
        /// Last time this gate pass was used
        /// </summary>
        public DateTime? LastUsedOn { get; set; }

        /// <summary>
        /// Remaining allowance in minutes
        /// </summary>
        [NotMapped]
        public int RemainingMinutes => Math.Max(0, TotalAllowanceMinutes - UsedMinutes);

        /// <summary>
        /// Can this gate pass still be used?
        /// (Has uses left AND has time remaining)
        /// </summary>
        [NotMapped]
        public bool CanUse => TimesUsed < MaxUsesPerMonth && RemainingMinutes > 0;

        /// <summary>
        /// Is this gate pass fully utilized?
        /// (Either max uses reached OR all time used)
        /// </summary>
        [NotMapped]
        public bool IsExhausted => TimesUsed >= MaxUsesPerMonth || RemainingMinutes <= 0;

        /// <summary>
        /// Display-friendly status
        /// </summary>
        [NotMapped]
        public string Status
        {
            get
            {
                if (IsExhausted) return "Exhausted";
                if (TimesUsed >= MaxUsesPerMonth) return "Max Uses Reached";
                if (RemainingMinutes <= 0) return "Time Depleted";
                return "Available";
            }
        }

        /// <summary>
        /// Formatted display of remaining time
        /// </summary>
        [NotMapped]
        public string RemainingTimeDisplay
        {
            get
            {
                int hours = RemainingMinutes / 60;
                int mins = RemainingMinutes % 60;
                return $"{hours}h {mins}m";
            }
        }

        /// <summary>
        /// Formatted display of uses
        /// </summary>
        [NotMapped]
        public string UsesDisplay => $"{TimesUsed}/{MaxUsesPerMonth} uses";
    }
}