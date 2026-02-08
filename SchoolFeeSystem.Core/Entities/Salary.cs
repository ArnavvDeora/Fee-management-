using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Salary information for employees
    /// </summary>
    public class Salary
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; }

        /// <summary>
        /// Basic salary amount
        /// </summary>
        public decimal BasicSalary { get; set; }

        /// <summary>
        /// House Rent Allowance
        /// </summary>
        public decimal HRA { get; set; } = 0;

        /// <summary>
        /// Conveyance Allowance
        /// </summary>
        public decimal ConveyanceAllowance { get; set; } = 0;

        /// <summary>
        /// Medical Allowance
        /// </summary>
        public decimal MedicalAllowance { get; set; } = 0;

        /// <summary>
        /// Other allowances
        /// </summary>
        public decimal OtherAllowances { get; set; } = 0;

        /// <summary>
        /// Gross salary (Basic + all allowances)
        /// </summary>
        [NotMapped]
        public decimal GrossSalary => BasicSalary + HRA + ConveyanceAllowance + MedicalAllowance + OtherAllowances;

        /// <summary>
        /// Provident Fund deduction
        /// </summary>
        public decimal PFDeduction { get; set; } = 0;

        /// <summary>
        /// Professional Tax
        /// </summary>
        public decimal ProfessionalTax { get; set; } = 0;

        /// <summary>
        /// Other deductions
        /// </summary>
        public decimal OtherDeductions { get; set; } = 0;

        /// <summary>
        /// Net salary (Gross - all deductions)
        /// </summary>
        [NotMapped]
        public decimal NetSalary => GrossSalary - PFDeduction - ProfessionalTax - OtherDeductions;

        /// <summary>
        /// When this salary record was created/updated
        /// </summary>
        public DateTime EffectiveFrom { get; set; } = DateTime.Now;

        /// <summary>
        /// Is this the current active salary?
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Remarks or notes about this salary
        /// </summary>
        public string Remarks { get; set; } = string.Empty;
    }
}