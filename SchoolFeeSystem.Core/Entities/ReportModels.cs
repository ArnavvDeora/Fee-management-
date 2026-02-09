using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Entities
{
    public class AttendanceReportItem
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int Holidays { get; set; }
        public int TotalPayable { get; set; }
    }

    public class SalaryReportItem
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public decimal BaseSalary { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetSalary { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // REMOVED SalarySlipItem from here because it is in its own file now.
}