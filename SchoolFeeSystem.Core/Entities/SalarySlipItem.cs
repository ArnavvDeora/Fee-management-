using System;

namespace SchoolFeeSystem.Core.Entities
{
    // This class is a "DTO" (Data Transfer Object) used to hold 
    // the calculated salary details before they are saved or printed.
    public class SalarySlipItem
    {
        public Employee Employee { get; set; }
        public string EmployeeName => Employee?.FullName ?? "Unknown";
        public string Designation => Employee?.Designation ?? "Staff";

        // Attendance Details
        public decimal TotalMonthDays { get; set; } = 30;
        public decimal DaysWorked { get; set; }
        public decimal PayableDays { get; set; }

        // Earnings
        public decimal BasicSalary { get; set; } // The fixed base
        public decimal SalaryEarned { get; set; } // Pro-rated based on attendance
        public decimal OT_Salary { get; set; }
        public decimal TotalAllowances { get; set; } // Sum of HRA, DA, etc.
        public decimal GrossSalary { get; set; }

        // Deductions
        public decimal EPF_Employee { get; set; }
        public decimal ESI_Employee { get; set; }
        public decimal TDS { get; set; }
        public decimal TotalDeductions { get; set; }

        // Net Pay (The actual amount transferred)
        // We include BOTH names to fix your specific error
        public decimal NetSalary { get; set; }
        public decimal NetPaid { get; set; }

        // Employer Contributions (Cost to Company)
        public decimal EPF_Employer { get; set; }
        public decimal ESI_Employer { get; set; }
        public decimal AdminCharges { get; set; }

        // Institute Cost
        public decimal InstituteCostBeforeGST { get; set; }
        public decimal GST_Amount { get; set; }
        public decimal TotalInstituteCost { get; set; }

        public string Status { get; set; } = "Draft"; // Draft, Paid, Hold
    }
}