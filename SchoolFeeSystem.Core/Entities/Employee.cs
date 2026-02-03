using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        // --- Basic Info ---
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string? BiometricId { get; set; }

        // --- Personal Details ---
        public string FatherName { get; set; }
        public DateTime DateOfBirth { get; set; } = new DateTime(1990, 1, 1);
        public string Gender { get; set; }
        public string MaritalStatus { get; set; }
        public string Category { get; set; }
        public string Qualification { get; set; }

        // --- IDs & Contact ---
        public string AadharNumber { get; set; }
        public string PanNumber { get; set; }
        public string Address { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }

        // --- Official Details ---
        public string Designation { get; set; }
        public string Department { get; set; }
        public string StaffType { get; set; } = "Teaching";
        public DateTime JoiningDate { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        // --- Financial & Banking ---
        public decimal BaseSalary { get; set; }
        public string BankAccountNo { get; set; }
        public string IfscCode { get; set; }
        public string UanNumber { get; set; }
        public string? PayGrade { get; set; }

        // --- Photo ---
        public byte[] Photo { get; set; }

        // --- Relationships ---
        public virtual List<Allowance> Allowances { get; set; } = new();
        public virtual List<Deduction> Deductions { get; set; } = new();

        // [FIXED] Changed from 'object' to 'List<SalaryRevision>'
        // This enables LINQ commands like OrderByDescending
        public virtual List<SalaryRevision> SalaryHistory { get; set; } = new();
    }
}