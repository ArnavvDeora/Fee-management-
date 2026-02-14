using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _context;
        private readonly OvertimeCalculationService _overtimeService;
        private readonly ICompanyGatePassService _gatePassService;

        // Constants
        private const decimal FULL_DAY_HOURS = 8m;
        private const decimal HALF_DAY_HOURS = 4m;

        public LeaveService(
            AppDbContext context,
            OvertimeCalculationService overtimeService,
            ICompanyGatePassService gatePassService)
        {
            _context = context;
            _overtimeService = overtimeService;
            _gatePassService = gatePassService;
        }

        /// <summary>
        /// Grant leave to an employee
        /// Automatically deducts from Company Gate Pass FIRST, then personal allowance
        /// </summary>
        public LeaveRequest GrantLeave(LeaveRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Validate employee exists
            var employee = _context.Employees.Find(request.EmployeeId);
            if (employee == null)
                throw new Exception($"Employee with ID {request.EmployeeId} not found");

            // Calculate leave hours based on type
            decimal leaveHours = CalculateLeaveHours(request);
            request.LeaveHours = leaveHours;
            int leaveMinutes = (int)(leaveHours * 60);

            // ===== STEP 1: TRY COMPANY GATE PASS FIRST =====
            int gatePassMinutesUsed = _gatePassService.TryUseGatePass(
                request.EmployeeId,
                leaveMinutes,
                request.Reason,
                request.LeaveDate
            );

            request.AllowanceMinutesUsed = gatePassMinutesUsed;
            int remainingMinutes = leaveMinutes - gatePassMinutesUsed;

            // ===== STEP 2: IF GATE PASS DOESN'T COVER ALL, USE PERSONAL ALLOWANCE =====
            if (remainingMinutes > 0)
            {
                var allowance = _context.OvertimeAllowances
                    .FirstOrDefault(a => a.EmployeeId == request.EmployeeId);

                if (allowance != null && allowance.AvailableMinutes > 0)
                {
                    int personalAllowanceUsed = Math.Min(remainingMinutes, allowance.AvailableMinutes);

                    allowance.UsedAllowanceMinutes += personalAllowanceUsed;
                    allowance.LastUpdated = DateTime.Now;
                    _context.OvertimeAllowances.Update(allowance);

                    request.AllowanceMinutesUsed += personalAllowanceUsed;
                    remainingMinutes -= personalAllowanceUsed;

                    // Determine source
                    if (gatePassMinutesUsed > 0 && personalAllowanceUsed > 0)
                    {
                        request.LeaveSource = "Company Gate Pass + Personal Allowance";
                    }
                    else if (gatePassMinutesUsed > 0)
                    {
                        request.LeaveSource = "Company Gate Pass";
                    }
                    else
                    {
                        request.LeaveSource = "Personal Allowance";
                    }

                    if (remainingMinutes > 0)
                    {
                        request.LeaveSource += " + Unpaid";
                    }
                }
                else
                {
                    // No personal allowance available
                    if (gatePassMinutesUsed > 0)
                    {
                        request.LeaveSource = "Company Gate Pass + Unpaid";
                    }
                    else
                    {
                        request.LeaveSource = "Unpaid";
                    }
                }
            }
            else
            {
                // Fully covered by company gate pass
                request.LeaveSource = "Company Gate Pass";
            }

            // Create attendance record for this leave
            CreateLeaveAttendanceRecord(request);

            // Save leave request
            _context.LeaveRequests.Add(request);
            _context.SaveChanges();

            return request;
        }

        /// <summary>
        /// Get all leaves for an employee (optionally filtered by year)
        /// </summary>
        public List<LeaveRequest> GetEmployeeLeaves(int employeeId, int? year = null)
        {
            var query = _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.EmployeeId == employeeId);

            if (year.HasValue)
            {
                query = query.Where(l => l.LeaveDate.Year == year.Value);
            }

            return query
                .OrderByDescending(l => l.LeaveDate)
                .ToList();
        }

        /// <summary>
        /// Get leaves in a date range
        /// </summary>
        public List<LeaveRequest> GetLeavesByDateRange(DateTime startDate, DateTime endDate)
        {
            return _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.LeaveDate >= startDate && l.LeaveDate <= endDate)
                .OrderBy(l => l.LeaveDate)
                .ToList();
        }

        /// <summary>
        /// Get leave statistics for an employee in a specific month
        /// </summary>
        public LeaveStatistics GetLeaveStatistics(int employeeId, int month, int year)
        {
            var employee = _context.Employees.Find(employeeId);
            if (employee == null) return null;

            var leaves = _context.LeaveRequests
                .Where(l => l.EmployeeId == employeeId &&
                           l.LeaveDate.Month == month &&
                           l.LeaveDate.Year == year &&
                           l.Status == "Approved")
                .ToList();

            var stats = new LeaveStatistics
            {
                EmployeeId = employeeId,
                EmployeeName = employee.FullName,
                Month = month,
                Year = year,
                TotalLeaves = leaves.Count,
                FullDayLeaves = leaves.Count(l => l.LeaveType == "Full Day"),
                HalfDayLeaves = leaves.Count(l => l.LeaveType == "Half Day"),
                CustomHoursLeaves = leaves.Where(l => l.LeaveType == "Custom Hours").Sum(l => l.LeaveHours),
                TotalLeaveHours = leaves.Sum(l => l.LeaveHours),
                AllowanceTimeUsedHours = leaves.Sum(l => l.AllowanceMinutesUsed) / 60m
            };

            // Calculate unpaid leave hours
            stats.UnpaidLeaveHours = leaves
                .Where(l => l.LeaveSource.Contains("Unpaid"))
                .Sum(l => l.LeaveHours - (l.AllowanceMinutesUsed / 60m));

            stats.PaidLeaveHours = stats.TotalLeaveHours - stats.UnpaidLeaveHours;

            // Calculate salary deduction
            stats.SalaryDeduction = CalculateLeaveDeductionInternal(employee, stats.UnpaidLeaveHours);

            return stats;
        }

        /// <summary>
        /// Cancel a leave and refund allowance time
        /// ✅ FIXED: Prevents multiple cancellations and properly refunds gate pass
        /// </summary>
        public bool CancelLeave(int leaveRequestId)
        {
            var leave = _context.LeaveRequests.Find(leaveRequestId);
            if (leave == null) return false;

            // ✅ FIX: Check if already cancelled (prevents infinite refund loop)
            if (leave.Status == "Cancelled")
            {
                return false; // Already cancelled, don't refund again!
            }

            // ===== STEP 1: Analyze the leave source to determine what to refund =====
            int totalMinutesUsed = leave.AllowanceMinutesUsed;
            int leaveMinutes = (int)(leave.LeaveHours * 60);

            int gatePassMinutesUsed = 0;
            int personalAllowanceMinutesUsed = 0;

            // Determine how much came from gate pass vs personal allowance
            // Priority was: Gate Pass FIRST, then Personal Allowance
            var gatePass = _gatePassService.GetOrCreateGatePass(
                leave.EmployeeId,
                leave.LeaveDate.Month,
                leave.LeaveDate.Year
            );

            // Calculate gate pass contribution
            // Logic: If gate pass was available at the time, it was used first
            if (leave.LeaveSource.Contains("Company Gate Pass"))
            {
                // Gate pass was used - figure out how much
                gatePassMinutesUsed = Math.Min(totalMinutesUsed, 120); // Max 120 mins from gate pass
                personalAllowanceMinutesUsed = totalMinutesUsed - gatePassMinutesUsed;
            }
            else
            {
                // Only personal allowance was used
                personalAllowanceMinutesUsed = totalMinutesUsed;
            }

            // ===== STEP 2: Refund Company Gate Pass (if used) =====
            if (gatePassMinutesUsed > 0 && gatePass.Id > 0)
            {
                try
                {
                    gatePass.UsedMinutes -= gatePassMinutesUsed;
                    gatePass.TimesUsed = Math.Max(0, gatePass.TimesUsed - 1);
                    gatePass.LastUsedOn = DateTime.Now;
                    _context.CompanyGatePasses.Update(gatePass);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Gate pass refund error: {ex.Message}");
                }
            }

            // ===== STEP 3: Refund Personal Allowance (if used) =====
            if (personalAllowanceMinutesUsed > 0)
            {
                var allowance = _context.OvertimeAllowances
                    .FirstOrDefault(a => a.EmployeeId == leave.EmployeeId);

                if (allowance != null)
                {
                    allowance.UsedAllowanceMinutes -= personalAllowanceMinutesUsed;
                    allowance.LastUpdated = DateTime.Now;
                    _context.OvertimeAllowances.Update(allowance);
                }
            }

            // ===== STEP 4: Delete related attendance record =====
            var attendanceRecord = _context.AttendanceRecords
                .FirstOrDefault(a => a.EmployeeId == leave.EmployeeId &&
                                    a.Date.Date == leave.LeaveDate.Date);
            if (attendanceRecord != null)
            {
                _context.AttendanceRecords.Remove(attendanceRecord);
            }

            // ===== STEP 5: Mark as cancelled =====
            leave.Status = "Cancelled";
            _context.SaveChanges();

            return true;
        }

        /// <summary>
        /// Update leave request
        /// </summary>
        public bool UpdateLeave(LeaveRequest request)
        {
            var existing = _context.LeaveRequests.Find(request.Id);
            if (existing == null) return false;

            // Update properties
            existing.LeaveType = request.LeaveType;
            existing.LeaveHours = request.LeaveHours;
            existing.Reason = request.Reason;
            existing.Remarks = request.Remarks;
            existing.Status = request.Status;

            _context.SaveChanges();
            return true;
        }

        /// <summary>
        /// Check if employee has sufficient allowance time
        /// </summary>
        public bool HasSufficientAllowance(int employeeId, decimal hoursRequired)
        {
            var allowance = _context.OvertimeAllowances
                .FirstOrDefault(a => a.EmployeeId == employeeId);

            if (allowance == null) return false;

            int minutesRequired = (int)(hoursRequired * 60);
            return allowance.AvailableMinutes >= minutesRequired;
        }

        /// <summary>
        /// Calculate salary deduction for unpaid leaves in a month
        /// </summary>
        public decimal CalculateLeaveDeduction(int employeeId, int month, int year)
        {
            var employee = _context.Employees.Find(employeeId);
            if (employee == null) return 0;

            var stats = GetLeaveStatistics(employeeId, month, year);
            return stats?.SalaryDeduction ?? 0;
        }

        // ============================================================
        // PRIVATE HELPER METHODS
        // ============================================================

        /// <summary>
        /// Calculate leave hours based on type
        /// </summary>
        private decimal CalculateLeaveHours(LeaveRequest request)
        {
            switch (request.LeaveType)
            {
                case "Full Day":
                    return FULL_DAY_HOURS;

                case "Half Day":
                    return HALF_DAY_HOURS;

                case "Custom Hours":
                    // Calculate from start and end time
                    if (TimeSpan.TryParse(request.StartTime, out var start) &&
                        TimeSpan.TryParse(request.EndTime, out var end))
                    {
                        return (decimal)(end - start).TotalHours;
                    }
                    return HALF_DAY_HOURS; // Default to half day if parsing fails

                default:
                    return HALF_DAY_HOURS;
            }
        }

        /// <summary>
        /// Create attendance record for leave day
        /// </summary>
        private void CreateLeaveAttendanceRecord(LeaveRequest request)
        {
            // Check if attendance record already exists for this date
            var existing = _context.AttendanceRecords
                .FirstOrDefault(a => a.EmployeeId == request.EmployeeId &&
                                    a.Date.Date == request.LeaveDate.Date);

            if (existing != null)
            {
                // Update existing record
                existing.Status = "On Leave";
                existing.LeaveType = request.LeaveType;
                existing.Remarks = $"Leave granted: {request.Reason}";
                existing.InTime = request.StartTime;
                existing.OutTime = request.EndTime;
                existing.Duration = $"{request.LeaveHours:F1} hrs";
                _context.AttendanceRecords.Update(existing);
            }
            else
            {
                // Create new attendance record
                var attendanceRecord = new AttendanceRecord
                {
                    EmployeeId = request.EmployeeId,
                    Date = request.LeaveDate,
                    Status = "On Leave",
                    LeaveType = request.LeaveType,
                    InTime = request.StartTime,
                    OutTime = request.EndTime,
                    Duration = $"{request.LeaveHours:F1} hrs",
                    Remarks = $"Leave granted: {request.Reason}",
                    IsManualEntry = true
                };

                _context.AttendanceRecords.Add(attendanceRecord);
            }
        }

        /// <summary>
        /// Calculate salary deduction for unpaid leave hours
        /// Formula: (Basic Salary / 26 days / 8 hours) * Unpaid Hours
        /// </summary>
        private decimal CalculateLeaveDeductionInternal(Employee employee, decimal unpaidHours)
        {
            if (unpaidHours <= 0) return 0;

            decimal basicSalary = 0;

            // Try to get salary from Salaries table first (if it exists)
            try
            {
                var salary = _context.Salaries?.FirstOrDefault(s => s.EmployeeId == employee.Id);
                if (salary != null)
                {
                    basicSalary = salary.BasicSalary;
                }
            }
            catch
            {
                // Salaries table doesn't exist, will use Employee.BaseSalary below
            }

            // Fallback: Use Employee.BaseSalary if Salaries table not found or no record
            if (basicSalary == 0)
            {
                basicSalary = employee.BaseSalary;
            }

            // If still no salary, return 0
            if (basicSalary == 0) return 0;

            // Hourly rate = Basic Salary / 26 working days / 8 hours
            decimal hourlyRate = basicSalary / 26m / 8m;
            return Math.Round(hourlyRate * unpaidHours, 2);
        }
    }
}