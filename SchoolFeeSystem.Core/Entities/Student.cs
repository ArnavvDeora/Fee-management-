using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string FatherName { get; set; } = string.Empty;

        [Required]
        public string ContactNumber { get; set; } = string.Empty;

        public DateTime DOB { get; set; } = DateTime.Now;
        public string Address { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        // Link to Class
        public int ClassId { get; set; }
        [ForeignKey("ClassId")]
        public virtual Class Class { get; set; }
        // 1. Navigation Collection (Links Student to their Fees)
        public virtual ICollection<StudentFee> StudentFees { get; set; } = new List<StudentFee>();

        // 2. Helper to calculate Total Dues for the List (Not stored in DB, just calculated)
        [NotMapped]
        public decimal TotalDues => StudentFees.Sum(sf => sf.PendingAmount);

        [NotMapped]
        public string DueStatusColor => TotalDues > 0 ? "#E74C3C" : "#2ECC71"; // Red if dues, Green if clear
    }
}