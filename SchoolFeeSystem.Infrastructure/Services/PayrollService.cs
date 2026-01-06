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

        // --- Basic CRUD ---
        public void AddEmployee(Employee employee)
        {
            _context.Employees.Add(employee);
            _context.SaveChanges();
        }

        public List<Employee> GetAllEmployees()
        {
            return _context.Employees.Where(e => e.IsActive).ToList();
        }

        // --- Payroll Processing ---
        public List<SalaryRecord> GenerateMonthlyPayroll(string monthYear)
        {
            // 1. Check if records already exist
            var existing = _context.SalaryRecords
                .Include(s => s.Employee)
                .Where(s => s.MonthYear == monthYear)
                .ToList();

            if (existing.Any()) return existing;

            // 2. Generate new records for active employees
            var employees = GetAllEmployees();
            var newRecords = new List<SalaryRecord>();

            foreach (var emp in employees)
            {
                var record = new SalaryRecord
                {
                    EmployeeId = emp.Id,
                    MonthYear = monthYear,
                    BaseAmount = emp.BaseSalary,
                    FinalAmount = emp.BaseSalary, // Default to base
                    Status = "Pending",
                    PaymentDate = null
                };
                _context.SalaryRecords.Add(record);
                newRecords.Add(record);
            }
            _context.SaveChanges();

            // Re-fetch to include Employee details
            return _context.SalaryRecords.Include(s => s.Employee).Where(s => s.MonthYear == monthYear).ToList();
        }
        public void UpdateEmployee(Employee employee)
        {
            _context.Employees.Update(employee);
            _context.SaveChanges();
        }
        public void PaySalary(int salaryRecordId)
        {
            var record = _context.SalaryRecords.Find(salaryRecordId);
            if (record != null)
            {
                record.Status = "Paid";
                record.PaymentDate = DateTime.Now;
                _context.SaveChanges();
            }
        }

        // --- DASHBOARD REAL DATA QUERIES ---

        public int GetTotalEmployees()
        {
            return _context.Employees.Count(e => e.IsActive);
        }

        public decimal GetTotalPayoutForMonth(string monthYear)
        {
            // Sum of FinalAmount for all PAID salaries in this month
            var records = _context.SalaryRecords
                .Where(s => s.MonthYear == monthYear && s.Status == "Paid")
                .ToList();

            if (records.Any())
            {
                return records.Sum(s => s.FinalAmount);
            }

            return 0;
        }

        public int GetPendingCount(string monthYear)
        {
            return _context.SalaryRecords.Count(s => s.MonthYear == monthYear && s.Status == "Pending");
        }

        public int GetPaidCount(string monthYear)
        {
            return _context.SalaryRecords.Count(s => s.MonthYear == monthYear && s.Status == "Paid");
        }

        public List<SalaryRecord> GetRecentPaidSalaries(int count)
        {
            // Get last 'count' payments, newest first
            return _context.SalaryRecords
                .Include(s => s.Employee)
                .Where(s => s.Status == "Paid")
                .OrderByDescending(s => s.PaymentDate)
                .Take(count)
                .ToList();
        }

        public List<Employee> GetRecentEmployees(int count)
        {
            // Get last 'count' employees added
            return _context.Employees
                .OrderByDescending(e => e.JoiningDate)
                .Take(count)
                .ToList();
        }
        public List<Employee> SearchStaff(string query, string staffType)
        {
            var dbQuery = _context.Employees.Where(e => e.StaffType == staffType && e.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                // Search by Name, Designation, or Department
                query = query.ToLower();
                dbQuery = dbQuery.Where(e =>
                    e.FirstName.ToLower().Contains(query) ||
                    e.LastName.ToLower().Contains(query) ||
                    e.Department.ToLower().Contains(query) ||
                    e.Designation.ToLower().Contains(query));
            }

            return dbQuery.ToList();
        }
        public Dictionary<string, decimal> GetPayoutHistory(int monthsToLookBack)
        {
            // This is a bit complex for EF Core SQLite, so we'll do it in memory for now (safe for small apps)
            var history = new Dictionary<string, decimal>();

            // Get all paid records
            var records = _context.SalaryRecords
                .Where(s => s.Status == "Paid")
                .ToList();

            // Group by MonthYear string manually
            var grouped = records
                .GroupBy(s => s.MonthYear)
                .Select(g => new { Month = g.Key, Total = g.Sum(x => x.FinalAmount) })
                .ToDictionary(k => k.Month, v => v.Total);

            return grouped;
        }
    }
}