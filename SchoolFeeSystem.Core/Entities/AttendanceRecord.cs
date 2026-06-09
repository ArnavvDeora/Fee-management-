using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class AttendanceRecord
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string InTime { get; set; } = "00:00";
        public string OutTime { get; set; } = "00:00";
        public string Duration { get; set; } = "00:00";
        public string Status { get; set; } = "Absent";
        public string LeaveType { get; set; } = "None"; // "Full Day", "Half Day", "Short Leave", "None"
        public string Remarks { get; set; } = string.Empty; // For manual edits (e.g. "Forgot punch")
        public bool IsManualEntry { get; set; } = false;

        // Flags for Rules
        public bool IsLate { get; set; }
        public bool IsEarlyExit { get; set; } = false;
        // Overtime tracking
        public int OvertimeMinutes { get; set; } = 0;          // Minutes worked after 5:00 PM
        public int LateMinutes { get; set; } = 0;              // Minutes late arrival
        public int LatePenaltyMinutes { get; set; } = 0;       // Penalty (rounded to 30-min blocks)
        public int AllowanceTimeUsed { get; set; } = 0;        // Allowance time used to offset penalty

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }
    }
}