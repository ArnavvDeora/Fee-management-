using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Tracks accumulated allowance time (overtime bank) for each employee
    /// For non-OT departments (not CNC Workshop or Heat Treatment)
    /// </summary>
    public class OvertimeAllowance
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }

        /// <summary>
        /// Total accumulated allowance time in minutes
        /// Earned from working after 5:00 PM
        /// </summary>
        public int TotalAllowanceMinutes { get; set; } = 0;

        /// <summary>
        /// Allowance time used to offset late arrivals or leave
        /// </summary>
        public int UsedAllowanceMinutes { get; set; } = 0;

        /// <summary>
        /// Current available balance
        /// </summary>
        [NotMapped]
        public int AvailableMinutes => TotalAllowanceMinutes - UsedAllowanceMinutes;

        /// <summary>
        /// Last updated date
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}