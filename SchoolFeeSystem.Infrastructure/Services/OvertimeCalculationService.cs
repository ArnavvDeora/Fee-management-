using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SchoolFeeSystem.Infrastructure.Services
{
    /// <summary>
    /// Handles overtime tracking and allowance time management
    /// Updated to match company's department structure
    /// </summary>
    public class OvertimeCalculationService
    {
        private readonly AppDbContext _context;

        // Constants
        private const int STANDARD_START_HOUR = 9;    // 9:00 AM
        private const int STANDARD_END_HOUR = 17;     // 5:30 PM (hour part)
        private const int STANDARD_END_MINUTE = 30;   // 5:30 PM (minute part)
        private const int GRACE_PERIOD_MINUTES = 15;  // 15-minute grace period for late arrivals
        private const int LATE_PENALTY_BLOCK = 30;    // Round up to 30-minute blocks

        // Departments eligible for PAID overtime (get cash, not allowance time)
        private static readonly string[] OT_PAID_DEPARTMENTS = {
            "TRAINING WORKSHOP",
            "CNC Workshop", // Legacy name support
            "HEAT-TREATMENT SHOP",
            "Heat Treatment" // Legacy name support
        };

        public OvertimeCalculationService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Calculate overtime and late penalties for an attendance record
        /// Called after attendance import or manual entry
        ///
        /// FIX 1: OT is banked BEFORE penalty offset (so same-day OT can cover penalties)
        /// FIX 2: Gate Pass is consumed FIRST before personal OT bank
        /// FIX 3: Single allowance object reused to avoid stale-object overwrites
        /// </summary>
        public void CalculateOvertimeAndPenalties(AttendanceRecord record)
        {
            if (record == null || record.Status != "Present") return;

            // Parse times
            if (!TimeSpan.TryParse(record.InTime, out var inTime) ||
                !TimeSpan.TryParse(record.OutTime, out var outTime))
            {
                return;
            }

            var employee = _context.Employees.Find(record.EmployeeId);
            if (employee == null) return;

            bool isOTPaid = IsOTPaidDepartment(employee.Department);

            // ===== STEP 1: Calculate Late Minutes =====
            var standardStart = new TimeSpan(STANDARD_START_HOUR, 0, 0);
            if (inTime > standardStart)
            {
                record.LateMinutes = (int)(inTime - standardStart).TotalMinutes;

                if (record.LateMinutes <= GRACE_PERIOD_MINUTES)
                {
                    record.LatePenaltyMinutes = 0;
                }
                else
                {
                    int penalisableMinutes = record.LateMinutes - GRACE_PERIOD_MINUTES;
                    record.LatePenaltyMinutes = (int)Math.Ceiling(penalisableMinutes / (double)LATE_PENALTY_BLOCK) * LATE_PENALTY_BLOCK;
                }
            }
            else
            {
                record.LateMinutes = 0;
                record.LatePenaltyMinutes = 0;
            }

            // ===== STEP 2: Calculate Overtime Minutes (after 5:30 PM) =====
            var standardEnd = new TimeSpan(STANDARD_END_HOUR, STANDARD_END_MINUTE, 0);
            record.OvertimeMinutes = outTime > standardEnd
                ? (int)(outTime - standardEnd).TotalMinutes
                : 0;

            // ===== STEP 3: Bank TODAY's OT FIRST =====
            // OT must be banked before penalty offset so same-day OT can cover penalties.
            // AsNoTracking in GetOrCreateAllowance ensures we always read fresh from DB.
            if (record.OvertimeMinutes > 0 && !isOTPaid)
            {
                var allowance = GetOrCreateAllowance(record.EmployeeId);
                allowance.TotalAllowanceMinutes += record.OvertimeMinutes;
                allowance.LastUpdated = DateTime.Now;

                // Detach any existing tracked instance before attaching updated one
                var tracked = _context.ChangeTracker.Entries<OvertimeAllowance>()
                    .FirstOrDefault(e => e.Entity.EmployeeId == record.EmployeeId);
                if (tracked != null)
                    tracked.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                _context.OvertimeAllowances.Update(allowance);
                _context.SaveChanges();
            }

            // ===== STEP 4: Offset Penalty — Gate Pass FIRST, then OT Bank =====
            if (record.LatePenaltyMinutes > 0 && !isOTPaid)
            {
                int remainingPenalty = record.LatePenaltyMinutes;
                int totalOffsetUsed = 0;

                // 4a: Gate Pass first (company rule — always consumed before personal OT bank)
                var gatePassService = new CompanyGatePassService(_context);
                int gatePassUsed = gatePassService.TryUseGatePass(
                    record.EmployeeId, remainingPenalty, "Late penalty offset", record.Date);

                remainingPenalty -= gatePassUsed;
                totalOffsetUsed += gatePassUsed;

                // 4b: Personal OT allowance bank for whatever penalty remains
                if (remainingPenalty > 0)
                {
                    // Fresh read from DB after Step 3 saved
                    var allowance = GetOrCreateAllowance(record.EmployeeId);
                    int bankUsed = Math.Min(remainingPenalty, allowance.AvailableMinutes);

                    if (bankUsed > 0)
                    {
                        allowance.UsedAllowanceMinutes += bankUsed;
                        allowance.LastUpdated = DateTime.Now;

                        // Detach stale tracked instance before update
                        var tracked = _context.ChangeTracker.Entries<OvertimeAllowance>()
                            .FirstOrDefault(e => e.Entity.EmployeeId == record.EmployeeId);
                        if (tracked != null)
                            tracked.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                        _context.OvertimeAllowances.Update(allowance);
                        totalOffsetUsed += bankUsed;
                    }
                }

                record.AllowanceTimeUsed = totalOffsetUsed;
                _context.SaveChanges();
            }

            _context.SaveChanges();
        }

        /// <summary>
        /// Calculate total paid overtime hours for an employee in a month
        /// Only for TRAINING WORKSHOP & HEAT-TREATMENT SHOP departments
        /// These employees get CASH for OT, not allowance time
        /// </summary>
        public decimal GetPaidOvertimeHours(int employeeId, int month, int year)
        {
            var employee = _context.Employees.Find(employeeId);
            if (employee == null || !IsOTPaidDepartment(employee.Department))
                return 0;

            var totalMinutes = _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId &&
                           a.Date.Month == month &&
                           a.Date.Year == year &&
                           a.Status == "Present")
                .Sum(a => a.OvertimeMinutes);

            return totalMinutes / 60m; // Convert to hours
        }

        /// <summary>
        /// Calculate OT Salary based on Excel formula:
        /// OT Salary = (Basic Salary ÷ 26 ÷ 8) × 2 × OT Hours
        /// 
        /// NOTE: This uses 26 WORKING DAYS, not 30 calendar days!
        /// OVERTIME IS PAID AT DOUBLE THE HOURLY RATE
        /// </summary>
        public decimal CalculateOTSalary(decimal basicSalary, decimal otHours)
        {
            if (otHours <= 0) return 0;

            // Formula: (Basic Salary / 26 working days / 8 hours per day) * 2 * OT Hours
            decimal hourlyRate = basicSalary / 26m / 8m;
            decimal overtimeRate = hourlyRate * 2m; // DOUBLE PAY for overtime
            return Math.Round(overtimeRate * otHours, 2);
        }

        /// <summary>
        /// Get current allowance time balance for an employee
        /// </summary>
        public OvertimeAllowance GetAllowanceBalance(int employeeId)
        {
            return GetOrCreateAllowance(employeeId);
        }

        /// <summary>
        /// Manually use allowance time for leave application
        /// </summary>
        public bool UseAllowanceTimeForLeave(int employeeId, int minutesRequired, string reason)
        {
            var allowance = GetOrCreateAllowance(employeeId);

            if (allowance.AvailableMinutes < minutesRequired)
                return false; // Insufficient balance

            allowance.UsedAllowanceMinutes += minutesRequired;
            allowance.LastUpdated = DateTime.Now;
            _context.OvertimeAllowances.Update(allowance);
            _context.SaveChanges();

            return true;
        }

        /// <summary>
        /// Get or create overtime allowance record.
        /// Uses AsNoTracking + explicit re-attach to avoid EF returning
        /// stale cached objects across multiple SaveChanges calls in one import.
        /// </summary>
        private OvertimeAllowance GetOrCreateAllowance(int employeeId)
        {
            // AsNoTracking forces a real DB read every time — no EF cache
            var allowance = _context.OvertimeAllowances
                .AsNoTracking()
                .FirstOrDefault(a => a.EmployeeId == employeeId);

            if (allowance == null)
            {
                allowance = new OvertimeAllowance
                {
                    EmployeeId = employeeId,
                    TotalAllowanceMinutes = 0,
                    UsedAllowanceMinutes = 0,
                    LastUpdated = DateTime.Now
                };
                _context.OvertimeAllowances.Add(allowance);
                _context.SaveChanges();

                // Re-read after save to get the DB-assigned Id
                allowance = _context.OvertimeAllowances
                    .AsNoTracking()
                    .FirstOrDefault(a => a.EmployeeId == employeeId);
            }

            return allowance;
        }

        /// <summary>
        /// Check if department is eligible for paid overtime
        /// TRAINING WORKSHOP and HEAT-TREATMENT SHOP get CASH for OT
        /// All other departments get ALLOWANCE TIME
        /// </summary>
        private bool IsOTPaidDepartment(string department)
        {
            if (string.IsNullOrEmpty(department)) return false;

            return OT_PAID_DEPARTMENTS.Any(d =>
                department.Equals(d, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get net penalty minutes after allowance offset
        /// This is what actually affects salary
        /// </summary>
        public int GetNetLatePenalty(AttendanceRecord record)
        {
            return Math.Max(0, record.LatePenaltyMinutes - record.AllowanceTimeUsed);
        }

        /// <summary>
        /// Format minutes to readable format
        /// </summary>
        public string FormatMinutes(int minutes)
        {
            int hours = minutes / 60;
            int mins = minutes % 60;
            return $"{hours}h {mins}m";
        }
    }
}