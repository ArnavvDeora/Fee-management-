using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class PayrollService : IPayrollService
    {
        private readonly AppDbContext _context;

        public PayrollService(AppDbContext context)
        {
            _context = context;
        }

        // --- NEW: Generate Salary Slip like Excel ---
        // ... (Inside PayrollService class) ...

        public SalarySlipItem GenerateDetailedSalary(int employeeId, int month, int year)
        {
            // 1. Load Employee with Related Data (Allowances/Deductions)
            var emp = _context.Employees
                .Include(e => e.Allowances) // CRITICAL: Load Allowances
                .Include(e => e.Deductions) // CRITICAL: Load Deductions
                .FirstOrDefault(e => e.Id == employeeId);

            if (emp == null) return null;

            var slip = new SalarySlipItem
            {
                Employee = emp,
                BasicSalary = emp.BaseSalary,
                TotalMonthDays = 30,
                Status = "Calculated"
            };

            // 2. GET ATTENDANCE (From DB)
            int present = _context.AttendanceRecords.Count(a => a.EmployeeId == employeeId && a.Date.Month == month && a.Date.Year == year && a.Status == "Present");
            int holidays = _context.Holidays.Count(h => h.Date.Month == month && h.Date.Year == year);

            // Logic: If no attendance found, assume full month (or use actual 0)
            int totalDaysFound = present + holidays;
            slip.DaysWorked = totalDaysFound == 0 ? 30 : (totalDaysFound > 30 ? 30 : totalDaysFound);

            slip.PayableDays = slip.DaysWorked;

            // 3. EARNINGS
            slip.SalaryEarned = Math.Round((slip.BasicSalary / 30m) * slip.PayableDays, 2);
            slip.OT_Salary = 0;

            // --- LINKING ALLOWANCES HERE ---
            decimal totalAllowances = emp.Allowances?.Sum(a => a.Amount) ?? 0;

            // Gross = Earned + OT + Allowances
            slip.GrossSalary = slip.SalaryEarned + slip.OT_Salary + totalAllowances;

            // 4. DEDUCTIONS (Include DB Deductions + Statutory)
            decimal dbDeductions = emp.Deductions?.Sum(d => d.Amount) ?? 0;

            slip.ESI_Employee = Math.Round(slip.GrossSalary * 0.0075m, 2);
            slip.EPF_Employee = Math.Round(slip.GrossSalary * 0.12m, 2);
            slip.TDS = Math.Round(slip.GrossSalary * 0.02m, 2);

            slip.TotalDeductions = slip.ESI_Employee + slip.EPF_Employee + slip.TDS + dbDeductions;

            // 5. NET PAY
            slip.NetPaid = slip.GrossSalary - slip.TotalDeductions;

            // 6. EMPLOYER CONTRIBUTIONS
            slip.EPF_Employer = Math.Round(slip.GrossSalary * 0.13m, 2);
            slip.ESI_Employer = Math.Round(slip.GrossSalary * 0.0325m, 2);
            slip.AdminCharges = Math.Round(slip.GrossSalary * 0.005m, 2);

            slip.InstituteCostBeforeGST = slip.NetPaid + slip.EPF_Employer + slip.ESI_Employer + slip.AdminCharges;
            slip.GST_Amount = Math.Round(slip.InstituteCostBeforeGST * 0.18m, 2);
            slip.TotalInstituteCost = slip.InstituteCostBeforeGST + slip.GST_Amount;

            return slip;
        }

        // --- EXISTING METHODS (Kept for compatibility) ---

        public void AddEmployee(Employee employee)
        {
            _context.Employees.Add(employee);
            _context.SaveChanges();
        }
        public void AddEmployeesBulk(List<Employee> employees)
        {
            _context.Employees.AddRange(employees);
            _context.SaveChanges();
        }
        public void UpdateEmployee(Employee employee)
        {
            _context.Employees.Update(employee);
            _context.SaveChanges();
        }
        public List<Employee> GetAllEmployees() => _context.Employees.Where(e => e.IsActive).ToList();

        public Employee GetEmployeeById(int id) => _context.Employees.Find(id);

        public int GetTotalEmployeeCount() => _context.Employees.Count();

        public List<Employee> GetEmployeesPaged(int page, int pageSize)
        {
            return _context.Employees.OrderBy(e => e.FirstName).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        public List<Employee> SearchStaff(string query, string type)
        {
            var dbQuery = _context.Employees.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                query = query.ToLower();
                dbQuery = dbQuery.Where(e => e.FirstName.ToLower().Contains(query) || e.LastName.ToLower().Contains(query));
            }
            return dbQuery.ToList();
        }

        public Employee GetEmployeeWithSalaryDetails(int id)
        {
            return _context.Employees
                .Include(e => e.Allowances)
                .Include(e => e.Deductions)
                .Include(e => e.SalaryHistory)
                .FirstOrDefault(e => e.Id == id);
        }

        // Configuration
        public List<SalaryComponent> GetSalaryComponents() => _context.SalaryComponents.Where(c => c.IsActive).ToList();
        public void SaveSalaryComponent(SalaryComponent c) { if (c.Id == 0) _context.SalaryComponents.Add(c); else _context.SalaryComponents.Update(c); _context.SaveChanges(); }
        public void DeleteSalaryComponent(int id) { var c = _context.SalaryComponents.Find(id); if (c != null) { _context.SalaryComponents.Remove(c); _context.SaveChanges(); } }
        public void SaveSalaryConfiguration(Employee e, string r) { _context.Employees.Update(e); _context.SaveChanges(); }

        // Calculation (Simplified for Grid, Detailed for Slip)
        public decimal CalculateNetSalary(int employeeId, int month, int year)
        {
            var slip = GenerateDetailedSalary(employeeId, month, year);
            return slip != null ? slip.NetSalary : 0;
        }

        public List<SalaryRecord> GenerateMonthlyPayroll(string monthYear) => new List<SalaryRecord>(); // Stub
        public void PaySalary(int id) { } // Stub

        // Reports
        public List<AttendanceReportItem> GetAttendanceReport(int m, int y, int? id) => new List<AttendanceReportItem>(); // Stub for brevity
        public List<SalaryReportItem> GetSalaryReport(int m, int y, int? id) => new List<SalaryReportItem>(); // Stub for brevity

        // Dashboard
        public int GetTotalEmployees() => _context.Employees.Count(e => e.IsActive);
        public decimal GetTotalPayoutForMonth(string my) => 0;
        public int GetPendingCount(string my) => 0;
        public int GetPaidCount(string my) => 0;
        public List<SalaryRecord> GetRecentPaidSalaries(int c) => new List<SalaryRecord>();
        public List<Employee> GetRecentEmployees(int c) => _context.Employees.OrderByDescending(e => e.JoiningDate).Take(c).ToList();
    }
}