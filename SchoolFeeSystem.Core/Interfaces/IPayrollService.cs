using SchoolFeeSystem.Core.Entities;
using System.Collections.Generic;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IPayrollService
    {
        // Basic Management
        void AddEmployee(Employee employee);
        List<Employee> GetAllEmployees();

        // Salary Processing
        List<SalaryRecord> GenerateMonthlyPayroll(string monthYear);
        void PaySalary(int salaryRecordId);

        // Dashboard Real Data Helpers
        int GetTotalEmployees();
        decimal GetTotalPayoutForMonth(string monthYear);
        int GetPendingCount(string monthYear);
        int GetPaidCount(string monthYear);

        // Lists for the Dashboard
        List<SalaryRecord> GetRecentPaidSalaries(int count);
        List<Employee> GetRecentEmployees(int count);
        Dictionary<string, decimal> GetPayoutHistory(int monthsToLookBack);
    }
}