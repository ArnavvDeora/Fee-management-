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

        // =========================================================
        // 1. SALARY SLIP GENERATION (Fixed Ambiguity)
        // =========================================================

        // [FIX] explicit path: SchoolFeeSystem.Core.Entities.SalarySlipItem
        public SchoolFeeSystem.Core.Entities.SalarySlipItem GenerateDetailedSalary(int employeeId, int month, int year)
        {
            var emp = _context.Employees
                .Include(e => e.Allowances)
                .Include(e => e.Deductions)
                .FirstOrDefault(e => e.Id == employeeId);

            if (emp == null) return null;

            // [FIX] explicit path here too
            var slip = new SchoolFeeSystem.Core.Entities.SalarySlipItem
            {
                Employee = emp,
                BasicSalary = emp.BaseSalary,
                TotalMonthDays = 30,
                Status = "Calculated"
            };

            // Attendance Logic
            int present = _context.AttendanceRecords.Count(a => a.EmployeeId == employeeId && a.Date.Month == month && a.Date.Year == year && a.Status == "Present");
            int holidays = _context.Holidays.Count(h => h.Date.Month == month && h.Date.Year == year);

            int totalDaysFound = present + holidays;
            slip.DaysWorked = totalDaysFound == 0 ? 30 : (totalDaysFound > 30 ? 30 : totalDaysFound);
            slip.PayableDays = slip.DaysWorked;

            // Earnings
            slip.SalaryEarned = Math.Round((slip.BasicSalary / 30m) * slip.PayableDays, 2);
            decimal totalAllowances = emp.Allowances?.Sum(a => a.Amount) ?? 0;
            slip.GrossSalary = slip.SalaryEarned + totalAllowances;

            // Deductions
            decimal dbDeductions = emp.Deductions?.Sum(d => d.Amount) ?? 0;
            slip.EPF_Employee = Math.Round(slip.GrossSalary * 0.12m, 2);
            slip.ESI_Employee = Math.Round(slip.GrossSalary * 0.0075m, 2);
            slip.TDS = 0;

            slip.TotalDeductions = slip.ESI_Employee + slip.EPF_Employee + slip.TDS + dbDeductions;

            // [FIX] Sync both names to be safe
            slip.NetSalary = slip.GrossSalary - slip.TotalDeductions;
            slip.NetPaid = slip.NetSalary;

            // Employer Contribution
            slip.EPF_Employer = Math.Round(slip.GrossSalary * 0.13m, 2);
            slip.ESI_Employer = Math.Round(slip.GrossSalary * 0.0325m, 2);

            return slip;
        }

        // =========================================================
        // 2. EMPLOYEE MANAGEMENT
        // =========================================================
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

            if (!string.IsNullOrEmpty(type))
            {
                dbQuery = dbQuery.Where(e => e.StaffType == type);
            }

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
                // [FIX] Add this line so history is loaded from the database
                .Include(e => e.SalaryHistory)
                .FirstOrDefault(e => e.Id == id);
        }

        // =========================================================
        // 3. SALARY CONFIGURATION & HISTORY
        // =========================================================

        public List<SalaryComponent> GetSalaryComponents() => _context.SalaryComponents.Where(c => c.IsActive).ToList();

        public void SaveSalaryComponent(SalaryComponent c)
        {
            if (c.Id == 0) _context.SalaryComponents.Add(c);
            else _context.SalaryComponents.Update(c);
            _context.SaveChanges();
        }

        public void DeleteSalaryComponent(int id)
        {
            var c = _context.SalaryComponents.Find(id);
            if (c != null) { _context.SalaryComponents.Remove(c); _context.SaveChanges(); }
        }

        public void SaveSalaryConfiguration(Employee employee, string reason)
        {
            _context.Employees.Update(employee);

            var revision = new SalaryRevision
            {
                EmployeeId = employee.Id,
                NewSalary = employee.BaseSalary,
                Reason = reason,
                RevisionDate = DateTime.Now,
                UpdatedBy = "Admin"
            };

            _context.SalaryRevisions.Add(revision);
            _context.SaveChanges();
        }

        public List<SalaryRevision> GetSalaryRevisions(int employeeId)
        {
            return _context.SalaryRevisions
                .Where(r => r.EmployeeId == employeeId)
                .OrderByDescending(r => r.RevisionDate)
                .ToList();
        }

        // =========================================================
        // 4. STUBS & DASHBOARD STATS
        // =========================================================

        public decimal CalculateNetSalary(int employeeId, int month, int year)
        {
            var slip = GenerateDetailedSalary(employeeId, month, year);
            return slip != null ? slip.NetSalary : 0;
        }

        public List<SalaryRecord> GenerateMonthlyPayroll(string monthYear) => new List<SalaryRecord>();
        public void PaySalary(int id) { }

        public List<AttendanceReportItem> GetAttendanceReport(int m, int y, int? id) => new List<AttendanceReportItem>();
        public List<SalaryReportItem> GetSalaryReport(int m, int y, int? id) => new List<SalaryReportItem>();

        public int GetTotalEmployees() => _context.Employees.Count(e => e.IsActive);
        public decimal GetTotalPayoutForMonth(string my) => 0;
        public int GetPendingCount(string my) => 0;
        public int GetPaidCount(string my) => 0;
        public List<SalaryRecord> GetRecentPaidSalaries(int c) => new List<SalaryRecord>();
        public List<Employee> GetRecentEmployees(int c) => _context.Employees.OrderByDescending(e => e.JoiningDate).Take(c).ToList();
    }
}