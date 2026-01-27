using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Entities
{
    public class AttendanceReportItem
    {
        public string EmployeeName { get; set; }
        public string Designation { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int Holidays { get; set; }
        public int TotalPayable { get; set; }
    }

    public class SalaryReportItem
    {
        public string EmployeeName { get; set; }
        public string Designation { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetSalary { get; set; }
        public string Status { get; set; }
    }

    public class SalarySlipItem
    {
        // 1. ENTITY: Employee
        public Employee Employee { get; set; }
        public string Designation => Employee?.Designation;
        public decimal BasicSalary { get; set; }

        // 2. ATTENDANCE MODEL
        public decimal TotalMonthDays { get; set; } = 30;
        public decimal DaysWorked { get; set; }
        public decimal RecoveryDays { get; set; } // Late, Penalties
        public decimal PayableDays { get; set; }  // Worked - Recovery

        // 3. SALARY STRUCTURE
        // Section A (Regular)
        public decimal SalaryEarned { get; set; } // (Basic / MonthDays) * PayableDays

        // Section B (Overtime)
        public decimal OT_Hours { get; set; }
        public decimal OT_Rate { get; set; }
        public decimal OT_Salary { get; set; } // Hours * Rate

        // Gross
        public decimal GrossSalary { get; set; } // Section A + Section B

        // 4. EMPLOYEE DEDUCTIONS
        public decimal ESI_Employee { get; set; } // 0.75%
        public decimal EPF_Employee { get; set; } // Configurable
        public decimal TDS { get; set; }          // 2%
        public decimal TotalDeductions { get; set; }

        // 5. NET PAY
        public decimal NetPaid { get; set; }      // Gross - Deductions

        // 6. EMPLOYER CONTRIBUTIONS
        public decimal EPF_Employer { get; set; } // 13%
        public decimal ESI_Employer { get; set; } // 3.25%
        public decimal AdminCharges { get; set; } // Configurable

        // 7. INSTITUTE TOTAL COST
        public decimal InstituteCostBeforeGST { get; set; }
        public decimal GST_Amount { get; set; }   // 18%
        public decimal TotalInstituteCost { get; set; }

        public string Status { get; set; }
        public int NetSalary { get; set; }
    }
}