using SchoolFeeSystem.Core.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Employee
{
    [Key]
    public int Id { get; set; }

    // --- Basic Info ---
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? BiometricId { get; set; }

    // --- Personal Details ---
    public string FatherName { get; set; } = "NA";
    public DateTime DateOfBirth { get; set; } = new DateTime(1990, 1, 1);
    public string Gender { get; set; } = "Unknown";
    public string MaritalStatus { get; set; } = "Unknown";
    public string Category { get; set; } = "General";
    public string Qualification { get; set; } = "NA";

    // --- IDs & Contact ---
    public string AadharNumber { get; set; } = "NA";
    public string PanNumber { get; set; } = "NA";
    public string Address { get; set; } = "NA";
    public string PhoneNumber { get; set; } = "0000000000";
    public string Email { get; set; } = "na@na.local";

    // --- Official Details ---
    public string Designation { get; set; } = "Staff";
    public string Department { get; set; } = "General";
    public string StaffType { get; set; } = "Teaching";
    public DateTime JoiningDate { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;

    // --- Financial & Banking ---
    public decimal BaseSalary { get; set; } = 0;
    public string BankAccountNo { get; set; } = "NA";
    public string IfscCode { get; set; } = "NA";
    public string UanNumber { get; set; } = "NA";
    public string? PayGrade { get; set; }

    // --- Photo ---
    public byte[] Photo { get; set; } = Array.Empty<byte>();

    // --- Relationships ---
    public virtual List<Allowance> Allowances { get; set; } = new();
    public virtual List<Deduction> Deductions { get; set; } = new();
    public virtual List<SalaryRevision> SalaryHistory { get; set; } = new();
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();

}
