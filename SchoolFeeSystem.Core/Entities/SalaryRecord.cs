using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class SalaryRecord
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;

        public string MonthYear { get; set; } = string.Empty; // e.g. "Jan-2026"

        public decimal BaseAmount { get; set; }
        public decimal Deductions { get; set; }
        public decimal Bonus { get; set; }
        public decimal FinalAmount { get; set; } // Base - Ded + Bonus

        public string Status { get; set; } = "Pending"; // "Pending", "Paid"
        public DateTime? PaymentDate { get; set; }
    }
}