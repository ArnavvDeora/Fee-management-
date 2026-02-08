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

        // Constants
        private const decimal FULL_DAY_HOURS = 8m;
        private const decimal HALF_DAY_HOURS = 4m;

        public LeaveService(AppDbContext context, OvertimeCalculationService overtimeService)
        {
            _context = context;
            _overtimeService = overtimeService;
        }

        /// <summary>
        /// Grant leave to an employee
        /// Automatically deducts from allowance time if available
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

            // Check allowance time availability
            var allowance = _context.OvertimeAllowances
                .FirstOrDefault(a => a.EmployeeId == request.EmployeeId);

            if (allowance != null && allowance.AvailableMinutes > 0)
            {
                // Use allowance time to cover leave
                int minutesToDeduct = Math.Min(leaveMinutes, allowance.AvailableMinutes);

                allowance.UsedAllowanceMinutes += minutesToDeduct;
                allowance.LastUpdated = DateTime.Now;
                _context.OvertimeAllowances.Update(allowance);

                request.AllowanceMinutesUsed = minutesToDeduct;
                request.LeaveSource = minutesToDeduct == leaveMinutes
                    ? "Allowance Time"
                    : "Partially Allowance, Partially Unpaid";

                // If allowance doesn't cover full leave, remaining is unpaid
                if (minutesToDeduct < leaveMinutes)
                {
                    request.LeaveSource = "Allowance Time + Unpaid";
                }
            }
            else
            {
                // No allowance time available - unpaid leave
                request.LeaveSource = "Unpaid";
                request.AllowanceMinutesUsed = 0;
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
        /// </summary>
        public bool CancelLeave(int leaveRequestId)
        {
            var leave = _context.LeaveRequests.Find(leaveRequestId);
            if (leave == null) return false;

            // Refund allowance time if it was used
            if (leave.AllowanceMinutesUsed > 0)
            {
                var allowance = _context.OvertimeAllowances
                    .FirstOrDefault(a => a.EmployeeId == leave.EmployeeId);

                if (allowance != null)
                {
                    allowance.UsedAllowanceMinutes -= leave.AllowanceMinutesUsed;
                    allowance.LastUpdated = DateTime.Now;
                    _context.OvertimeAllowances.Update(allowance);
                }
            }

            // Delete related attendance record
            var attendanceRecord = _context.AttendanceRecords
                .FirstOrDefault(a => a.EmployeeId == leave.EmployeeId &&
                                    a.Date.Date == leave.LeaveDate.Date);
            if (attendanceRecord != null)
            {
                _context.AttendanceRecords.Remove(attendanceRecord);
            }

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
        /// 
        /// ✅ FIXED: Now uses Employee.BaseSalary directly OR Salaries table (whichever is available)
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