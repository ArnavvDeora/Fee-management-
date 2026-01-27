using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        // Helper to get full name easily
        public string FullName => $"{FirstName} {LastName}";

        public string Designation { get; set; } = string.Empty; // e.g. "Senior Teacher"
        public string Department { get; set; } = string.Empty;  // e.g. "Math, Science"
        public string StaffType { get; set; } = "Teaching";     // "Teaching" or "Non-Teaching"

        public decimal BaseSalary { get; set; }
        public DateTime JoiningDate { get; set; } = DateTime.Now;
        public string? BiometricId { get; set; }

        // Navigation property
        public virtual ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();


        // Contact Info
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public virtual ICollection<Allowance> Allowances { get; set; } = new List<Allowance>();
        public virtual ICollection<Deduction> Deductions { get; set; } = new List<Deduction>();
        public virtual ICollection<SalaryRevision> SalaryHistory { get; set; } = new List<SalaryRevision>();
        public string PayGrade { get; set; } = "Grade A";
        public bool IsActive { get; set; } = true;
    }
}