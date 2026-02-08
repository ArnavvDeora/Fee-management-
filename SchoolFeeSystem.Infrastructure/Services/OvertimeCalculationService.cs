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
        private const int STANDARD_START_HOUR = 9;  // 9:00 AM
        private const int STANDARD_END_HOUR = 17;   // 5:00 PM
        private const int LATE_PENALTY_BLOCK = 30;   // Round up to 30-minute blocks

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

            // ===== STEP 1: Calculate Late Minutes =====
            var standardStart = new TimeSpan(STANDARD_START_HOUR, 0, 0);
            if (inTime > standardStart)
            {
                record.LateMinutes = (int)(inTime - standardStart).TotalMinutes;

                // Round up to 30-minute penalty blocks
                // Example: 15 mins late = 30 min penalty, 45 mins late = 60 min penalty
                record.LatePenaltyMinutes = (int)Math.Ceiling(record.LateMinutes / (double)LATE_PENALTY_BLOCK) * LATE_PENALTY_BLOCK;
            }
            else
            {
                record.LateMinutes = 0;
                record.LatePenaltyMinutes = 0;
            }

            // ===== STEP 2: Calculate Overtime Minutes (after 5:00 PM) =====
            var standardEnd = new TimeSpan(STANDARD_END_HOUR, 0, 0);
            if (outTime > standardEnd)
            {
                record.OvertimeMinutes = (int)(outTime - standardEnd).TotalMinutes;
            }
            else
            {
                record.OvertimeMinutes = 0;
            }

            // ===== STEP 3: Handle Late Penalty vs Allowance Time =====
            // Only for NON-OT departments (they have allowance time bank)
            if (record.LatePenaltyMinutes > 0 && !IsOTPaidDepartment(employee.Department))
            {
                // Try to use allowance time to offset late penalty
                var allowance = GetOrCreateAllowance(record.EmployeeId);

                int availableAllowance = allowance.AvailableMinutes;
                int penaltyToOffset = Math.Min(record.LatePenaltyMinutes, availableAllowance);

                if (penaltyToOffset > 0)
                {
                    // Use allowance time to cover the late penalty
                    record.AllowanceTimeUsed = penaltyToOffset;
                    allowance.UsedAllowanceMinutes += penaltyToOffset;
                    allowance.LastUpdated = DateTime.Now;
                    _context.OvertimeAllowances.Update(allowance);
                }
            }

            // ===== STEP 4: Add Overtime to Allowance Bank =====
            // Only for NON-OT departments (they get allowance time, not cash)
            if (record.OvertimeMinutes > 0 && !IsOTPaidDepartment(employee.Department))
            {
                var allowance = GetOrCreateAllowance(record.EmployeeId);
                allowance.TotalAllowanceMinutes += record.OvertimeMinutes;
                allowance.LastUpdated = DateTime.Now;
                _context.OvertimeAllowances.Update(allowance);
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
        /// OT Salary = (Basic Salary ÷ 26 ÷ 8) × OT Hours
        /// 
        /// NOTE: This uses 26 WORKING DAYS, not 30 calendar days!
        /// </summary>
        public decimal CalculateOTSalary(decimal basicSalary, decimal otHours)
        {
            if (otHours <= 0) return 0;

            // Formula from Excel: (Basic Salary / 26 working days / 8 hours per day) * OT Hours
            decimal hourlyRate = basicSalary / 26m / 8m;
            return Math.Round(hourlyRate * otHours, 2);
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
        /// Get or create overtime allowance record
        /// </summary>
        private OvertimeAllowance GetOrCreateAllowance(int employeeId)
        {
            var allowance = _context.OvertimeAllowances
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