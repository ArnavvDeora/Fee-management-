using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class SalaryRevision
    {
        [Key]
        public int Id { get; set; }
        public DateTime RevisionDate { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty; // e.g. "Base salary increased by 10%"
        public string ChangedBy { get; set; } = "Admin";

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;
    }
}