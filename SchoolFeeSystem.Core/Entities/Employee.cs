using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty; // e.g. Teacher, Driver
        public string ContactNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;

        public decimal BaseSalary { get; set; } // Fixed monthly salary
        public DateTime JoiningDate { get; set; } = DateTime.Today;

        public bool IsActive { get; set; } = true;
    }
}