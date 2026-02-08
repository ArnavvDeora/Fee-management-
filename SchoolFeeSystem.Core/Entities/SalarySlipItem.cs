using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Complete salary slip matching Excel format
    /// Includes Employee's Share + Employer's Share
    /// </summary>
    public class SalarySlipItem
    {
        // ========================================
        // EMPLOYEE INFORMATION
        // ========================================
        public Employee Employee { get; set; }

        // ========================================
        // BASIC SALARY INFORMATION
        // ========================================
        public decimal BasicSalary { get; set; }
        public int TotalMonthDays { get; set; } = 30;
        public decimal DaysWorked { get; set; }
        public decimal PayableDays { get; set; }

        // ========================================
        // OVERTIME & RECOVERY
        // ========================================
        /// <summary>
        /// Total overtime hours worked (for OT-paid departments only)
        /// </summary>
        public decimal OTHours { get; set; } = 0;

        /// <summary>
        /// OT Salary = (Basic ÷ 26 ÷ 8) × OT Hours
        /// </summary>
        public decimal OTSalary { get; set; } = 0;

        /// <summary>
        /// Recovery hours (penalty hours deducted)
        /// </summary>
        public decimal RecoveryHours { get; set; } = 0;

        /// <summary>
        /// Recovery Salary = (Basic ÷ 30 ÷ 8) × Recovery Hours
        /// </summary>
        public decimal RecoverySalary { get; set; } = 0;

        // ========================================
        // EARNINGS
        // ========================================
        /// <summary>
        /// Salary Earned = (Basic × Days) ÷ 30
        /// </summary>
        public decimal SalaryEarned { get; set; }

        /// <summary>
        /// Gross Salary = Salary Earned + OT Salary - Recovery Salary
        /// </summary>
        public decimal GrossSalary { get; set; }

        // ========================================
        // EMPLOYEE'S SHARE (Deductions)
        // ========================================
        /// <summary>
        /// EPF Employee Contribution (12% of EPF Wage Base, max ₹15,000)
        /// </summary>
        public decimal EPF_Employee { get; set; }

        /// <summary>
        /// ESI Employee Contribution (0.75% of Gross, if Basic ≤ ₹21,000)
        /// </summary>
        public decimal ESI_Employee { get; set; }

        /// <summary>
        /// Tax Deducted at Source (if applicable)
        /// </summary>
        public decimal TDS { get; set; } = 0;

        /// <summary>
        /// Incentive (added to salary)
        /// </summary>
        public decimal Incentive { get; set; } = 0;

        /// <summary>
        /// Total Deductions = EPF + ESI + TDS - Incentive
        /// </summary>
        public decimal TotalDeductions { get; set; }

        // ========================================
        // NET PAY (What Employee Receives)
        // ========================================
        /// <summary>
        /// Net Paid = Gross - Total Deductions
        /// This is what goes to employee's bank account
        /// </summary>
        public decimal NetPaid { get; set; }

        /// <summary>
        /// Same as NetPaid (for backward compatibility)
        /// </summary>
        public decimal NetSalary { get; set; }

        // ========================================
        // EMPLOYER'S SHARE (Company Costs)
        // ========================================
        /// <summary>
        /// EPF Employer Contribution (13% of EPF Wage Base, max ₹15,000)
        /// </summary>
        public decimal EPF_Employer { get; set; }

        /// <summary>
        /// ESI Employer Contribution (3.25% of Gross, if Basic ≤ ₹21,000)
        /// </summary>
        public decimal ESI_Employer { get; set; }

        /// <summary>
        /// Administrative charges (1.89% of Gross Salary)
        /// </summary>
        public decimal AdminCharges { get; set; } = 0;

        /// <summary>
        /// GST on employer cost (18%)
        /// </summary>
        public decimal GST_Amount { get; set; } = 0;

        /// <summary>
        /// Total Employer Cost (before GST)
        /// = Gross + EPF Employer + ESI Employer + Admin Charges + Incentive
        /// </summary>
        [NotMapped]
        public decimal InstituteCostBeforeGST =>
            GrossSalary + EPF_Employer + ESI_Employer + AdminCharges + Incentive;

        /// <summary>
        /// Total Institute Cost (after GST)
        /// = InstituteCostBeforeGST + GST_Amount
        /// </summary>
        [NotMapped]
        public decimal TotalInstituteCost => InstituteCostBeforeGST + GST_Amount;

        /// <summary>
        /// Total Employer Cost (same as TotalInstituteCost)
        /// </summary>
        [NotMapped]
        public decimal TotalEmployerCost => TotalInstituteCost;

        // ========================================
        // DISPLAY PROPERTIES
        // ========================================
        /// <summary>
        /// Designation from employee record
        /// </summary>
        [NotMapped]
        public string Designation => Employee?.Designation ?? "N/A";

        /// <summary>
        /// Department from employee record
        /// </summary>
        [NotMapped]
        public string Department => Employee?.Department ?? "N/A";

        /// <summary>
        /// Recovery days calculated from recovery hours
        /// </summary>
        [NotMapped]
        public decimal RecoveryDays => RecoveryHours / 8m;

        /// <summary>
        /// Payable days formatted to 2 decimal places
        /// </summary>
        [NotMapped]
        public string PayableDaysFormatted => PayableDays.ToString("F2");

        /// <summary>
        /// OT Hours display (hide if zero)
        /// </summary>
        [NotMapped]
        public string OTHoursDisplay => OTHours > 0 ? $"{OTHours:F1}" : "-";

        /// <summary>
        /// Recovery display (hide if zero)
        /// </summary>
        [NotMapped]
        public string RecoveryDisplay => RecoverySalary > 0 ? $"{RecoverySalary:F2}" : "-";

        // ========================================
        // STATUS
        // ========================================
        public string Status { get; set; } = "Pending";
    }
}