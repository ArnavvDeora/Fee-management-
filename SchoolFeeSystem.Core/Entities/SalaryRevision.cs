using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class SalaryRevision
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }

        public DateTime RevisionDate { get; set; } = DateTime.Now;

        // [FIX] Added this so we know what the salary changed TO
        public decimal NewSalary { get; set; }

        // [FIX] Renamed 'Description' to 'Reason' to match your Service code
        public string Reason { get; set; }

        // [FIX] Renamed 'ChangedBy' to 'UpdatedBy' to match your Service code
        public string UpdatedBy { get; set; } = "Admin";
    }
}