using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Leave request/grant record for tracking employee leaves
    /// </summary>
    public class LeaveRequest
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }

        /// <summary>
        /// Date of the leave
        /// </summary>
        public DateTime LeaveDate { get; set; }

        /// <summary>
        /// Leave type: "Full Day", "Half Day", "Custom Hours"
        /// </summary>
        public string LeaveType { get; set; } = "Half Day";

        /// <summary>
        /// Total hours of leave granted
        /// </summary>
        public decimal LeaveHours { get; set; }

        /// <summary>
        /// Start time of leave (for partial day leaves)
        /// Example: "10:00" for leave from 10 AM
        /// </summary>
        public string StartTime { get; set; } = "09:00";

        /// <summary>
        /// End time of leave (for partial day leaves)
        /// Example: "14:00" for leave until 2 PM
        /// </summary>
        public string EndTime { get; set; } = "17:00";

        /// <summary>
        /// Source of leave deduction: "Allowance Time", "Unpaid", "Paid Leave"
        /// </summary>
        public string LeaveSource { get; set; } = "Allowance Time";

        /// <summary>
        /// Minutes deducted from allowance time bank (if applicable)
        /// </summary>
        public int AllowanceMinutesUsed { get; set; } = 0;

        /// <summary>
        /// Reason for leave
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Admin who granted the leave
        /// </summary>
        public string GrantedBy { get; set; } = "Admin";

        /// <summary>
        /// When the leave was granted
        /// </summary>
        public DateTime GrantedOn { get; set; } = DateTime.Now;

        /// <summary>
        /// Status: "Approved", "Pending", "Rejected", "Cancelled"
        /// </summary>
        public string Status { get; set; } = "Approved";

        /// <summary>
        /// Additional remarks
        /// </summary>
        public string Remarks { get; set; } = string.Empty;

        /// <summary>
        /// Will this affect salary calculation?
        /// True if deducted from salary (unpaid leave)
        /// False if covered by allowance time
        /// </summary>
        [NotMapped]
        public bool AffectsSalary => LeaveSource == "Unpaid";

        /// <summary>
        /// Display-friendly leave duration
        /// </summary>
        [NotMapped]
        public string LeaveDurationDisplay
        {
            get
            {
                if (LeaveType == "Full Day") return "Full Day (8 hrs)";
                if (LeaveType == "Half Day") return "Half Day (4 hrs)";
                return $"{LeaveHours} hours ({StartTime} - {EndTime})";
            }
        }
    }
}