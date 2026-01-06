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

        // Contact Info
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}