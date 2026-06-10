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
        private const decimal EPF_WAGE_CAP = 15000m;   // Max base for EPF calculation
        private const decimal ESI_SALARY_LIMIT = 21000m;   // ESI exempt if Basic > 21,000
        private const decimal DAILY_WAGE_THRESHOLD = 1000m; // Basic < 1000 = daily-rate worker (e.g. sweepers)

        // Days / Time constants
        private const decimal WORKING_DAYS_FOR_OT = 26m;    // Denominator for OT hourly rate
        private const decimal HOURS_PER_DAY = 8m;    // Standard work hours per day

        public PayrollService(AppDbContext context)
        {
            _context = context;
            _overtimeService = new OvertimeCalculationService(context);
        }

        // =========================================================
        // ✅ NEW: HARD DELETE EMPLOYEE AND ALL RELATED DATA
        // =========================================================
        /// <summary>
        /// Permanently deletes an employee and ALL associated data from the database.
        /// This is a CASCADE DELETE operation that removes:
        /// - Employee record
        /// - All attendance records
        /// - All leave requests
        /// - All salary history/revisions
        /// - All allowances
        /// - All deductions
        /// - All overtime allowance records
        /// - All company gate pass usage
        /// WARNING: This operation CANNOT be undone!
        /// </summary>
        public bool DeleteEmployeePermanently(int employeeId)
        {
            try
            {
                // Start a transaction to ensure all-or-nothing deletion
                using (var transaction = _context.Database.BeginTransaction())
                {
                    try
                    {
                        // Get the employee
                        var employee = _context.Employees.Find(employeeId);
                        if (employee == null)
                        {
                            return false;
                        }

                        string employeeName = employee.FullName;

                        // Delete Attendance Records
                        var attendanceRecords = _context.AttendanceRecords
                            .Where(a => a.EmployeeId == employeeId)
                            .ToList();

                        if (attendanceRecords.Any())
                        {
                            _context.AttendanceRecords.RemoveRange(attendanceRecords);
                        }

                        // Delete Leave Requests
                        var leaveRequests = _context.LeaveRequests
                            .Where(l => l.EmployeeId == employeeId)
                            .ToList();

                        if (leaveRequests.Any())
                        {
                            _context.LeaveRequests.RemoveRange(leaveRequests);
                        }

                        // Delete Salary Revisions
                        var salaryRevisions = _context.SalaryRevisions
                            .Where(s => s.EmployeeId == employeeId)
                            .ToList();

                        if (salaryRevisions.Any())
                        {
                            _context.SalaryRevisions.RemoveRange(salaryRevisions);
                        }

                        // Delete Allowances
                        var allowances = _context.Allowances
                            .Where(a => a.EmployeeId == employeeId)
                            .ToList();

                        if (allowances.Any())
                        {
                            _context.Allowances.RemoveRange(allowances);
                        }

                        // Delete Deductions
                        var deductions = _context.Deductions
                            .Where(d => d.EmployeeId == employeeId)
                            .ToList();

                        if (deductions.Any())
                        {
                            _context.Deductions.RemoveRange(deductions);
                        }

                        // Delete Overtime Allowances (if table exists)
                        try
                        {
                            var overtimeAllowances = _context.OvertimeAllowances
                                .Where(o => o.EmployeeId == employeeId)
                                .ToList();

                            if (overtimeAllowances.Any())
                            {
                                _context.OvertimeAllowances.RemoveRange(overtimeAllowances);
                            }
                        }
                        catch { /* Table might not exist */ }

                        // Delete Company Gate Pass Usage (if table exists)
                        try
                        {
                            var gatePassUsage = _context.CompanyGatePasses
                                .Where(g => g.EmployeeId == employeeId)
                                .ToList();

                            if (gatePassUsage.Any())
                            {
                                _context.CompanyGatePasses.RemoveRange(gatePassUsage);
                            }
                        }
                        catch { /* Table might not exist */ }

                        // Delete Salary Records (if table exists)
                        try
                        {
                            var salaryRecords = _context.SalaryRecords
                                .Where(s => s.EmployeeId == employeeId)
                                .ToList();

                            if (salaryRecords.Any())
                            {
                                _context.SalaryRecords.RemoveRange(salaryRecords);
                            }
                        }
                        catch { /* Table might not exist */ }

                        // Finally, delete the Employee record itself
                        _context.Employees.Remove(employee);

                        // Save all changes
                        _context.SaveChanges();

                        // Commit transaction
                        transaction.Commit();

                        System.Diagnostics.Debug.WriteLine($"✅ Successfully deleted employee {employeeName} and all related data");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // Rollback transaction on error
                        transaction.Rollback();
                        System.Diagnostics.Debug.WriteLine($"❌ Error during deletion: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to delete employee {employeeId}: {ex.Message}");
                return false;
            }
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

            // ── Actual calendar days for this month ───────────────────────────────
            // SS Master divides by real month days: Jan=31, Feb=28/29, Apr=30, etc.
            // NEVER use a fixed 30-day constant.
            int calendarDays = DateTime.DaysInMonth(year, month);
            decimal monthDays = (decimal)calendarDays;

            // ── Daily-rate vs monthly-salary ──────────────────────────────────────
            // SS Master sweepers/drivers have Basic < 1000 which is a DAILY RATE.
            // Verified: LAKHWINDER basic=674 (daily), 674 × 27 days = 18,198 ✓
            bool isDailyWage = emp.BaseSalary < DAILY_WAGE_THRESHOLD;

            var slip = new SchoolFeeSystem.Core.Entities.SalarySlipItem
            {
                Employee = emp,
                BasicSalary = emp.BaseSalary,
                TotalMonthDays = calendarDays
            };

            // ===== STEP 1: GET ATTENDANCE DATA =====
            // PRIORITY ORDER:
            //   1. SS Master Excel import record (Remarks = "SS_MASTER_IMPORT") — most accurate
            //   2. Biometric attendance records — fallback when Excel not imported
            //
            // The SS Master record stores:
            //   LateMinutes         = DaysWorked×2 (÷2 to decode; ×2 preserves .5 half-days)
            //   OvertimeMinutes     = OT hours × 60
            //   LatePenaltyMinutes  = Recovery hours × 60

            decimal daysWorkedFromData;   // decimal to support half-days like 30.5
            decimal otHoursFromData;
            decimal penaltyHours;

            var ssMasterRecord = _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId &&
                            a.Date.Month == month &&
                            a.Date.Year == year &&
                            a.Remarks == "SS_MASTER_IMPORT")
                .FirstOrDefault();

            if (ssMasterRecord != null)
            {
                // ── SS Master Excel data ─────────────────────────────────────────
                daysWorkedFromData = ssMasterRecord.LateMinutes / 2m;      // ÷2 to decode days (stored as days×2 to preserve .5 half-days)
                otHoursFromData = ssMasterRecord.OvertimeMinutes / 60m;
                penaltyHours = ssMasterRecord.LatePenaltyMinutes / 60m;
            }
            else
            {
                // ── Biometric fallback ───────────────────────────────────────────
                var attendanceRecords = _context.AttendanceRecords
                    .Where(a => a.EmployeeId == employeeId &&
                               a.Date.Month == month &&
                               a.Date.Year == year &&
                               a.Remarks != "SS_MASTER_IMPORT")
                    .ToList();

                // FIX: Check for ZERO_ATTENDANCE sentinel — this means the attendance file
                // WAS imported for this month and the employee genuinely had 0 present days.
                // In this case daysWorkedFromData must stay 0 (not fall back to calendarDays).
                bool hasZeroAttendanceSentinel = attendanceRecords
                    .Any(a => a.Remarks == "ZERO_ATTENDANCE");

                if (hasZeroAttendanceSentinel)
                {
                    // Attendance was confirmed checked — employee was fully absent this month
                    daysWorkedFromData = 0;
                    penaltyHours = 0;
                    otHoursFromData = 0;
                }
                else if (!attendanceRecords.Any())
                {
                    // No attendance data at all — employee was added after the import.
                    // Return 0 days — do NOT fabricate a full month of attendance.
                    daysWorkedFromData = 0;
                    penaltyHours = 0;
                    otHoursFromData = 0;
                }
                else
                {
                    int presentDays = attendanceRecords.Count(a => a.Status == "Present");
                    int absentDays = attendanceRecords.Count(a => a.Status == "Absent");

                    // ✅ HOLIDAY FIX: Get actual holiday dates for precise matching.
                    // The old code did `calendarDays - absentDays + holidays` which double-counted.
                    // NEW: Holiday-status records (auto-marked by AddHoliday or attendance import)
                    // are already NOT counted in absentDays. But for holidays added BEFORE
                    // attendance import (where absent records weren't created at all), we need
                    // to count holiday records too.
                    var holidayDatesInMonth = _context.Holidays
                        .Where(h => h.Date.Month == month && h.Date.Year == year)
                        .Select(h => h.Date.Date)
                        .ToHashSet();

                    // Count "Holiday" status records (absences converted to holidays)
                    int holidayRecordCount = attendanceRecords.Count(a => a.Status == "Holiday");

                    // Count absences that STILL fall on holiday dates (edge case: holiday added
                    // but RecalculateAttendanceForHolidayDate hasn't run yet)
                    int absentOnHolidays = attendanceRecords
                        .Count(a => a.Status == "Absent" && holidayDatesInMonth.Contains(a.Date.Date));

                    // Adjusted absent = raw absent minus those on holidays
                    int trueAbsentDays = absentDays - absentOnHolidays;

                    if (isDailyWage)
                    {
                        // DAILY-WAGE WORKERS: paid only for days physically present.
                        // Weekly Offs and Holidays do NOT count as paid days.
                        // MIS days with a valid punch are already saved as "Present" by
                        // the biometric import, so presentDays includes them.
                        // Verified against SS Master: P+MIS = Master Days for all 4 workers.
                        daysWorkedFromData = presentDays;
                    }
                    else
                    {
                        // MONTHLY SALARIED: payable = calendarDays − trueAbsentDays
                        //
                        // From questionnaire: "Calendar days - Absent (WO, Holiday not deducted)"
                        // Meaning: WO and Holiday days are PAID. Only genuine absences reduce pay.
                        // Holiday records are not counted as absent. WO rows are not saved
                        // to the DB (import skips them), so they don't affect the count.
                        daysWorkedFromData = calendarDays - trueAbsentDays;
                    }

                    int totalLatePenaltyMinutes = attendanceRecords
                        .Where(a => a.Status == "Present")
                        .Sum(a => a.LatePenaltyMinutes);
                    penaltyHours = totalLatePenaltyMinutes / 60m;

                    otHoursFromData = _overtimeService.GetPaidOvertimeHours(employeeId, month, year);
                }
            }

            slip.RecoveryHours = penaltyHours;

            // ===== STEP 3: CALCULATE DAYS WORKED =====
            // DaysWorked is a decimal so 30.5-day employees are handled correctly.
            // The slip itself stores it as decimal — all formulas (SalaryEarned, EPF base)
            // will use the full 30.5, not a truncated 30.
            //
            // FIX: Do NOT fall back to calendarDays when daysWorkedFromData == 0.
            // A value of 0 means "no attendance on record" — the employee worked 0 days,
            // not that we should assume a full month. This was causing newly-added employees
            // (who joined after the attendance import) to show 31 payable days incorrectly.
            // Old (WRONG): daysWorkedFromData == 0 ? calendarDays : daysWorkedFromData
            // New (RIGHT): cap at calendarDays, but never inflate a genuine 0.
            slip.DaysWorked = daysWorkedFromData > calendarDays ? calendarDays : daysWorkedFromData;

            slip.PayableDays = slip.DaysWorked;

            // ===== STEP 4: SALARY EARNED =====
            // Monthly employee : Basic × DaysWorked ÷ calendarDays
            // Daily-rate worker: DailyRate × DaysWorked  (no division)
            // Verified: ASHISH  21400 × 5 ÷ 31 = 3,452 ✓
            //           LAKHWINDER 674 × 27    = 18,198 ✓
            if (isDailyWage)
                slip.SalaryEarned = Math.Round(slip.BasicSalary * slip.DaysWorked, 2);
            else
                slip.SalaryEarned = Math.Round((slip.BasicSalary * slip.DaysWorked) / monthDays, 2);

            // ===== STEP 5: OVERTIME SALARY =====
            // OT = DOUBLE pay: (Basic ÷ 26 ÷ 8) × 2 × OT_hours
            // otHoursFromData already set above (from SS Master or biometric)
            slip.OTHours = otHoursFromData;
            slip.OTSalary = otHoursFromData > 0
                ? Math.Round((slip.BasicSalary / WORKING_DAYS_FOR_OT / HOURS_PER_DAY) * 2m * otHoursFromData, 2)
                : 0;

            // ===== STEP 6: RECOVERY SALARY (late-hours deduction) =====
            // REC. column in Excel = HOURS late (not days).
            // Hourly rate = Basic ÷ calendarDays ÷ 8  (monthly employee)
            //             = DailyRate ÷ 8              (daily-rate worker)
            // Verified against all 27 recovery rows in SS Master — 0 exceptions.
            if (penaltyHours > 0)
            {
                decimal hourlyRate = isDailyWage
                    ? slip.BasicSalary / HOURS_PER_DAY
                    : slip.BasicSalary / monthDays / HOURS_PER_DAY;
                slip.RecoverySalary = Math.Round(hourlyRate * penaltyHours, 2);
            }
            else
            {
                slip.RecoverySalary = 0;
            }

            // ===== STEP 7: GROSS SALARY =====
            decimal baseGross = slip.SalaryEarned + slip.OTSalary - slip.RecoverySalary;

            decimal customAllowances = emp.Allowances?.Sum(a => a.Amount) ?? 0;
            decimal customDeductions = emp.Deductions?.Sum(d => d.Amount) ?? 0;

            slip.GrossSalary = baseGross + customAllowances;
            slip.Incentive = customAllowances;

            // ===== STEP 8: EPF WAGE BASE =====
            // Formula: (min(BasicMonthly, 15,000) × DaysWorked ÷ calendarDays) − RecoverySalary
            // RecoverySalary reduces the EPF wage base — verified on 27/27 recovery rows, 0 exceptions.
            // Example: RITU GOYAL: (15000×29÷31) - 141 = 13891.26 → ×12% = 1667 ✓
            //          MADHU BALA: min(18700,15000)×30.5÷31 - 0    = 14758.06 → ×12% = 1771 ✓
            //          ASHISH:     min(21400,15000)×5÷31    - 0    = 2419.35  → ×12% = 290  ✓
            decimal basicMonthlyForEpf = isDailyWage ? emp.BaseSalary * 26m : emp.BaseSalary;
            decimal epfWageBase = Math.Max(0,
                Math.Round(
                    Math.Min(basicMonthlyForEpf, EPF_WAGE_CAP) * slip.DaysWorked / monthDays
                    - slip.RecoverySalary,
                2));

            // ===== STEP 9: EMPLOYEE DEDUCTIONS =====
            slip.EPF_Employee = Math.Round(epfWageBase * EPF_EMPLOYEE_RATE, 2);

            // ESI RULE — verified against all 67 rows, ZERO exceptions:
            //   ESI applies when BasicSalary <= 21,000
            //   If BasicSalary > 21,000 → ESI = 0  (EsiNumber will be "N.A." in Excel)
            //
            // Key insight: PANKAJ RAM has gross=25,612 but basic=17,524 → pays ESI.
            // ESI eligibility is on BASIC salary, not gross or net.
            // For daily-rate workers use monthly equivalent (DailyRate × 26) as the check.
            decimal basicForEsi = isDailyWage ? emp.BaseSalary * 26m : emp.BaseSalary;
            bool esiExempt = basicForEsi > ESI_SALARY_LIMIT;

            slip.ESI_Employee = esiExempt ? 0 : Math.Round(slip.GrossSalary * ESI_EMPLOYEE_RATE, 2);

            slip.TDS = 0;
            slip.Incentive = 0;

            slip.TotalDeductions = slip.EPF_Employee + slip.ESI_Employee + slip.TDS + customDeductions;

            // ===== STEP 10: NET PAID =====
            slip.NetPaid = slip.GrossSalary - slip.TotalDeductions;
            slip.NetSalary = slip.NetPaid;

            // ===== STEP 11: EPF EMPLOYER =====
            slip.EPF_Employer = Math.Round(epfWageBase * EPF_EMPLOYER_RATE, 2);

            // ===== STEP 12: ESI EMPLOYER =====
            slip.ESI_Employer = esiExempt ? 0 : Math.Round(slip.GrossSalary * ESI_EMPLOYER_RATE, 2);

            // ===== STEP 13: ADMIN CHARGES =====
            // Verified: AdminCharges = GrossSalary × 1.89%  (0 mismatches across all 67 rows)
            slip.AdminCharges = Math.Round(slip.GrossSalary * ADMIN_CHARGES_RATE, 2);

            // ===== STEP 14: TOTAL COST / GST =====
            // SS Master "TOTAL AMT." = Gross + EPF_ER + ESI_ER + Admin  (NOT NetPaid + ...)
            // Verified: RAKESH 17,524 + 1,950 + 570 + 331 = 20,375 ✓
            // GST is NOT in SS Master — set to 0.
            slip.GST_Amount = 0;

            slip.Status = "Calculated";

            return slip;
        }

        // =========================================================
        // SS MASTER ATTENDANCE IMPORT
        // =========================================================

        /// <summary>
        /// Stores the Days/OT/Rec columns from SS Master Excel into AttendanceRecords.
        /// Uses a single synthetic "Present" record per employee per month, tagged
        /// with Remarks="SS_MASTER_IMPORT" so GenerateDetailedSalary can find it.
        ///
        /// No new DB table or migration needed — we reuse the existing AttendanceRecords
        /// table with OvertimeMinutes = OT×60 and LatePenaltyMinutes = Rec×60.
        ///
        /// Idempotent: re-importing the same Excel for the same month replaces the record.
        /// </summary>
        public void SaveSsMasterAttendance(
            int employeeId, int month, int year,
            decimal daysWorked, decimal otHours, decimal recHours)
        {
            // Remove any existing SS Master record for this employee/month
            var existing = _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId &&
                            a.Date.Month == month &&
                            a.Date.Year == year &&
                            a.Remarks == "SS_MASTER_IMPORT")
                .ToList();
            if (existing.Any())
                _context.AttendanceRecords.RemoveRange(existing);

            // Create a synthetic record that carries the Excel attendance data
            var record = new AttendanceRecord
            {
                EmployeeId = employeeId,
                Date = new DateTime(year, month, 1),   // 1st of the month
                Status = "Present",
                InTime = "09:00",
                OutTime = "17:30",
                Duration = "08:30",
                IsManualEntry = true,
                Remarks = "SS_MASTER_IMPORT",

                // DaysWorked×2 stored in LateMinutes — preserves half-days (30.5 → 61, decode ÷2)
                // Using ×2 because Excel only ever has .5 increments for days worked.
                LateMinutes = (int)Math.Round(daysWorked * 2),

                // OT hours → OvertimeMinutes (×60)
                OvertimeMinutes = (int)Math.Round(otHours * 60),

                // Recovery hours → LatePenaltyMinutes (×60)
                LatePenaltyMinutes = (int)Math.Round(recHours * 60),

                AllowanceTimeUsed = 0,
                IsLate = false,
                IsEarlyExit = false,
                LeaveType = ""
            };

            _context.AttendanceRecords.Add(record);
            _context.SaveChanges();
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
        // ──────────────────────────────────────────────────────────────────────────────
        // ADD THESE TWO METHODS TO YOUR PayrollService.cs
        // (place them alongside the other employee management methods)
        // ──────────────────────────────────────────────────────────────────────────────

        public List<FlaggedBiometricEntry> GetUnresolvedFlaggedBiometrics()
        {
            return _context.FlaggedBiometricEntries
                .Where(f => !f.IsResolved)
                .OrderByDescending(f => f.FirstSeenOn)
                .ToList();
        }

        public void ResolveFlaggedBiometric(int flaggedEntryId, int? linkedEmployeeId)
        {
            var entry = _context.FlaggedBiometricEntries.Find(flaggedEntryId);
            if (entry == null) return;

            entry.IsResolved = true;
            entry.ResolvedToEmployeeId = linkedEmployeeId;
            entry.ResolvedOn = DateTime.Now;

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