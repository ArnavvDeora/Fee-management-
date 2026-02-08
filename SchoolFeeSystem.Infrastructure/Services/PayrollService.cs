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
        private readonly OvertimeCalculationService _overtimeService;

        // ========================================
        // EXACT CONSTANTS FROM EXCEL
        // ========================================

        // Employee's Share (Deductions from salary)
        private const decimal EPF_EMPLOYEE_RATE = 0.12m;      // 12%
        private const decimal ESI_EMPLOYEE_RATE = 0.0075m;    // 0.75%

        // Employer's Share (Company pays on top)
        private const decimal EPF_EMPLOYER_RATE = 0.13m;      // 13%
        private const decimal ESI_EMPLOYER_RATE = 0.0325m;    // 3.25%
        private const decimal ADMIN_CHARGES_RATE = 0.0189m;   // 1.89%
        private const decimal GST_RATE = 0.18m;               // 18%

        // Statutory Limits
        private const decimal EPF_WAGE_CAP = 15000m;          // Max base for EPF
        private const decimal ESI_SALARY_LIMIT = 21000m;      // ESI only if Basic ≤ this

        // Days for calculation
        private const decimal TOTAL_DAYS_IN_MONTH = 30m;      // Calendar days
        private const decimal WORKING_DAYS_FOR_OT = 26m;      // Working days for OT
        private const decimal HOURS_PER_DAY = 8m;             // Hours per day

        public PayrollService(AppDbContext context)
        {
            _context = context;
            _overtimeService = new OvertimeCalculationService(context);
        }

        // =========================================================
        // EXACT SALARY CALCULATION MATCHING EXCEL
        // =========================================================
        public SchoolFeeSystem.Core.Entities.SalarySlipItem GenerateDetailedSalary(
            int employeeId, int month, int year)
        {
            var emp = _context.Employees
                .Include(e => e.Allowances)
                .Include(e => e.Deductions)
                .FirstOrDefault(e => e.Id == employeeId);

            if (emp == null) return null;

            var slip = new SchoolFeeSystem.Core.Entities.SalarySlipItem
            {
                Employee = emp,
                BasicSalary = emp.BaseSalary,
                TotalMonthDays = 30
            };

            // ===== STEP 1: GET ATTENDANCE DATA =====
            var attendanceRecords = _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId &&
                           a.Date.Month == month &&
                           a.Date.Year == year)
                .ToList();

            int presentDays = attendanceRecords.Count(a => a.Status == "Present");
            int holidays = _context.Holidays.Count(h =>
                h.Date.Month == month &&
                h.Date.Year == year);

            // ===== STEP 2: CALCULATE LATE PENALTY (RECOVERY) =====
            // Net penalty = Total penalty - Allowance time used
            int totalLatePenaltyMinutes = attendanceRecords
                .Where(a => a.Status == "Present")
                .Sum(a => Math.Max(0, a.LatePenaltyMinutes - a.AllowanceTimeUsed));

            // Convert to hours for display
            decimal penaltyHours = totalLatePenaltyMinutes / 60m;
            slip.RecoveryHours = penaltyHours;

            // ===== STEP 3: CALCULATE DAYS WORKED =====
            // Days Worked = Present Days + Holidays
            int totalDaysFound = presentDays + holidays;
            slip.DaysWorked = totalDaysFound == 0 ? 30 :
                             (totalDaysFound > 30 ? 30 : totalDaysFound);

            // Initially set payable days = days worked (will adjust later)
            slip.PayableDays = slip.DaysWorked;

            // ===== STEP 4: SALARY EARNED (Before any deductions) =====
            // Formula: (Basic Salary × Days Worked) ÷ 30
            slip.SalaryEarned = Math.Round(
                (slip.BasicSalary * slip.DaysWorked) / TOTAL_DAYS_IN_MONTH,
                2);

            // ===== STEP 5: OVERTIME SALARY (Only for TRAINING WORKSHOP & HEAT-TREATMENT SHOP) =====
            // Formula: (Basic ÷ 26 ÷ 8) × OT Hours
            decimal otHours = _overtimeService.GetPaidOvertimeHours(employeeId, month, year);
            slip.OTHours = otHours;

            if (otHours > 0)
            {
                decimal hourlyRateForOT = slip.BasicSalary / WORKING_DAYS_FOR_OT / HOURS_PER_DAY;
                slip.OTSalary = Math.Round(hourlyRateForOT * otHours, 2);
            }
            else
            {
                slip.OTSalary = 0;
            }

            // ===== STEP 6: RECOVERY SALARY (Penalty deduction) =====
            // Formula: (Basic ÷ 30 ÷ 8) × Penalty Hours
            if (penaltyHours > 0)
            {
                decimal hourlyRateForRecovery = slip.BasicSalary / TOTAL_DAYS_IN_MONTH / HOURS_PER_DAY;
                slip.RecoverySalary = Math.Round(hourlyRateForRecovery * penaltyHours, 2);
            }
            else
            {
                slip.RecoverySalary = 0;
            }

            // ===== STEP 7: GROSS SALARY (WITH CUSTOM INCENTIVES) =====
            decimal baseGross = slip.SalaryEarned + slip.OTSalary - slip.RecoverySalary;

            // Add custom monthly allowances/incentives
            decimal customAllowances = emp.Allowances?.Sum(a => a.Amount) ?? 0;
            decimal customDeductions = emp.Deductions?.Sum(d => d.Amount) ?? 0;

            // Final Gross = Base + Custom Incentives
            slip.GrossSalary = baseGross + customAllowances;
            slip.Incentive = customAllowances;

            // ===== STEP 8: EPF WAGE BASE CALCULATION (CRITICAL!) =====
            // This determines EPF for both employee AND employer
            decimal epfWageBase;

            if (slip.BasicSalary >= EPF_WAGE_CAP)
            {
                // If basic >= 15,000: EPF Base = ((15,000 × Days) ÷ 30) - Recovery
                epfWageBase = ((EPF_WAGE_CAP * slip.DaysWorked) / TOTAL_DAYS_IN_MONTH) - slip.RecoverySalary;
            }
            else
            {
                // If basic < 15,000: EPF Base = Salary Earned - Recovery
                epfWageBase = slip.SalaryEarned - slip.RecoverySalary;
            }

            // EPF base cannot be negative
            epfWageBase = Math.Max(0, epfWageBase);

            // ===== STEP 9: EMPLOYEE'S SHARE (Deductions) =====

            // EPF Employee @ 12% of EPF Wage Base
            slip.EPF_Employee = Math.Round(epfWageBase * EPF_EMPLOYEE_RATE, 2);

            // ESI Employee @ 0.75% of Gross (only if Basic ≤ 21,000)
            if (slip.BasicSalary <= ESI_SALARY_LIMIT)
            {
                slip.ESI_Employee = Math.Round(slip.GrossSalary * ESI_EMPLOYEE_RATE, 2);
            }
            else
            {
                slip.ESI_Employee = 0;
            }

            // Other deductions
            slip.TDS = 0;  // Set if applicable
            slip.Incentive = 0;  // Can be positive (addition) or negative (deduction)

            // Total Deductions = EPF + ESI + TDS - Incentive
            slip.TotalDeductions = slip.EPF_Employee + slip.ESI_Employee + slip.TDS + customDeductions;

            // ===== STEP 10: NET PAID TO EMPLOYEE =====
            // Formula: Gross Salary - Total Deductions
            slip.NetPaid = slip.GrossSalary - slip.TotalDeductions;
            slip.NetSalary = slip.NetPaid;  // Same value

            // =========================================================
            // EMPLOYER'S SHARE (What company pays on top)
            // =========================================================

            // ===== STEP 11: EPF EMPLOYER @ 13% =====
            // CRITICAL: Uses SAME EPF Wage Base as employee
            slip.EPF_Employer = Math.Round(epfWageBase * EPF_EMPLOYER_RATE, 2);

            // ===== STEP 12: ESI EMPLOYER @ 3.25% =====
            // Only if Basic ≤ 21,000 (same condition as employee ESI)
            if (slip.BasicSalary <= ESI_SALARY_LIMIT)
            {
                slip.ESI_Employer = Math.Round(slip.GrossSalary * ESI_EMPLOYER_RATE, 2);
            }
            else
            {
                slip.ESI_Employer = 0;
            }

            // ===== STEP 13: ADMIN CHARGES @ 1.89% =====
            // Calculated on Gross Salary
            slip.AdminCharges = Math.Round(slip.GrossSalary * ADMIN_CHARGES_RATE, 2);

            // ===== STEP 14: GST @ 18% =====
            // Calculated on total employer cost before GST
            // Total Cost Before GST = Gross + EPF Employer + ESI Employer + Admin + Incentive
            decimal costBeforeGST = slip.GrossSalary + slip.EPF_Employer +
                                   slip.ESI_Employer + slip.AdminCharges + slip.Incentive;

            slip.GST_Amount = Math.Round(costBeforeGST * GST_RATE, 2);

            // Status
            slip.Status = "Calculated";

            return slip;
        }

        // =========================================================
        // OVERTIME MANAGEMENT
        // =========================================================
        public OvertimeAllowance GetOvertimeAllowance(int employeeId)
        {
            return _overtimeService.GetAllowanceBalance(employeeId);
        }

        public bool UseAllowanceTime(int employeeId, int minutes, string reason)
        {
            return _overtimeService.UseAllowanceTimeForLeave(employeeId, minutes, reason);
        }

        // =========================================================
        // EMPLOYEE MANAGEMENT
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

        public List<Employee> GetAllEmployees() =>
            _context.Employees.Where(e => e.IsActive).ToList();

        public Employee GetEmployeeById(int id) =>
            _context.Employees.Find(id);

        public int GetTotalEmployeeCount() =>
            _context.Employees.Count();

        public List<Employee> GetEmployeesPaged(int page, int pageSize)
        {
            return _context.Employees
                .OrderBy(e => e.FirstName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
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
                dbQuery = dbQuery.Where(e =>
                    e.FirstName.ToLower().Contains(query) ||
                    e.LastName.ToLower().Contains(query));
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

        // =========================================================
        // SALARY CONFIGURATION & HISTORY
        // =========================================================
        public List<SalaryComponent> GetSalaryComponents() =>
            _context.SalaryComponents.Where(c => c.IsActive).ToList();

        public void SaveSalaryComponent(SalaryComponent c)
        {
            if (c.Id == 0) _context.SalaryComponents.Add(c);
            else _context.SalaryComponents.Update(c);
            _context.SaveChanges();
        }

        public void DeleteSalaryComponent(int id)
        {
            var c = _context.SalaryComponents.Find(id);
            if (c != null)
            {
                _context.SalaryComponents.Remove(c);
                _context.SaveChanges();
            }
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
        // STUBS & DASHBOARD STATS
        // =========================================================
        public decimal CalculateNetSalary(int employeeId, int month, int year)
        {
            var slip = GenerateDetailedSalary(employeeId, month, year);
            return slip != null ? slip.NetSalary : 0;
        }

        public List<SalaryRecord> GenerateMonthlyPayroll(string monthYear) =>
            new List<SalaryRecord>();

        public void PaySalary(int id) { }

        public List<AttendanceReportItem> GetAttendanceReport(int m, int y, int? id) =>
            new List<AttendanceReportItem>();

        public List<SalaryReportItem> GetSalaryReport(int m, int y, int? id) =>
            new List<SalaryReportItem>();

        public int GetTotalEmployees() =>
            _context.Employees.Count(e => e.IsActive);

        public decimal GetTotalPayoutForMonth(string my) => 0;
        public int GetPendingCount(string my) => 0;
        public int GetPaidCount(string my) => 0;

        public List<SalaryRecord> GetRecentPaidSalaries(int c) =>
            new List<SalaryRecord>();

        public List<Employee> GetRecentEmployees(int c) =>
            _context.Employees
                .OrderByDescending(e => e.JoiningDate)
                .Take(c)
                .ToList();
    }
}