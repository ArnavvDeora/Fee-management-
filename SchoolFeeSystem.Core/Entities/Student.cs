using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Linq;

namespace SchoolFeeSystem.Core.Entities
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;

        // --- NEW FIELDS ---
        public string Address { get; set; } = string.Empty;
        public string FatherContact { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        // ------------------

        public bool IsActive { get; set; } = true;

        public int ClassId { get; set; }
        [ForeignKey("ClassId")]
        public virtual Class Class { get; set; }

        public virtual ICollection<StudentFee> StudentFees { get; set; } = new List<StudentFee>();

        [NotMapped]
        public decimal TotalDues => StudentFees.Sum(sf => sf.PendingAmount);

        [NotMapped]
        public string DueStatusColor => TotalDues > 0 ? "#E74C3C" : "#2ECC71";
    }
}