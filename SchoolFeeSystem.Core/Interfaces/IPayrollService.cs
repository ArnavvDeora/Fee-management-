using SchoolFeeSystem.Core.Entities;
using System.Collections.Generic;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IPayrollService
    {
        // --- Employee Management ---
        void AddEmployee(Employee employee);
        void AddEmployeesBulk(List<Employee> employees);
        List<Employee> GetAllEmployees();
        Employee GetEmployeeById(int id);
        void UpdateEmployee(Employee employee);
        int GetTotalEmployeeCount();
        List<Employee> GetEmployeesPaged(int page, int pageSize);
        List<Employee> SearchStaff(string query, string type);
        Employee GetEmployeeWithSalaryDetails(int id);

        // --- Salary Configuration ---
        List<SalaryComponent> GetSalaryComponents();
        void SaveSalaryComponent(SalaryComponent component);
        void DeleteSalaryComponent(int id);
        void SaveSalaryConfiguration(Employee employee, string reason);
        List<SalaryRevision> GetSalaryRevisions(int employeeId);

        // --- Payroll Processing ---
        decimal CalculateNetSalary(int employeeId, int month, int year);
        List<SalaryRecord> GenerateMonthlyPayroll(string monthYear);
        void PaySalary(int salaryRecordId);

        // [FIX] Use the FULL NAME here to avoid Ambiguity Error
        SchoolFeeSystem.Core.Entities.SalarySlipItem GenerateDetailedSalary(int employeeId, int month, int year);

        // --- Reports ---
        List<AttendanceReportItem> GetAttendanceReport(int month, int year, int? employeeId = null);
        List<SalaryReportItem> GetSalaryReport(int month, int year, int? employeeId = null);

        // --- Dashboard Stats ---
        int GetTotalEmployees();
        decimal GetTotalPayoutForMonth(string monthYear);
        int GetPendingCount(string monthYear);
        int GetPaidCount(string monthYear);
        List<SalaryRecord> GetRecentPaidSalaries(int count);
        List<Employee> GetRecentEmployees(int count);
        OvertimeAllowance GetOvertimeAllowance(int id);
    }
}