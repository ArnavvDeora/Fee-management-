using SchoolFeeSystem.Core.Entities;
using System;
using System.Collections.Generic;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface ILeaveService
    {
        /// <summary>
        /// Grant leave to an employee
        /// Automatically handles allowance time deduction
        /// </summary>
        LeaveRequest GrantLeave(LeaveRequest request);

        /// <summary>
        /// Get all leave requests for an employee
        /// </summary>
        List<LeaveRequest> GetEmployeeLeaves(int employeeId, int? year = null);

        /// <summary>
        /// Get all leave requests in a date range
        /// </summary>
        List<LeaveRequest> GetLeavesByDateRange(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Get leave statistics for an employee in a month
        /// </summary>
        LeaveStatistics GetLeaveStatistics(int employeeId, int month, int year);

        /// <summary>
        /// Cancel a leave request
        /// Refunds allowance time if applicable
        /// </summary>
        bool CancelLeave(int leaveRequestId);

        /// <summary>
        /// Update leave request
        /// </summary>
        bool UpdateLeave(LeaveRequest request);

        /// <summary>
        /// Check if employee has sufficient allowance time for leave
        /// </summary>
        bool HasSufficientAllowance(int employeeId, decimal hoursRequired);

        /// <summary>
        /// Calculate leave impact on salary for a month
        /// </summary>
        decimal CalculateLeaveDeduction(int employeeId, int month, int year);
    }

    /// <summary>
    /// Leave statistics for an employee
    /// </summary>
    public class LeaveStatistics
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }

        // Leave counts
        public int TotalLeaves { get; set; }
        public int FullDayLeaves { get; set; }
        public int HalfDayLeaves { get; set; }
        public decimal CustomHoursLeaves { get; set; }

        // Leave hours breakdown
        public decimal TotalLeaveHours { get; set; }
        public decimal PaidLeaveHours { get; set; }
        public decimal UnpaidLeaveHours { get; set; }
        public decimal AllowanceTimeUsedHours { get; set; }

        // Financial impact
        public decimal SalaryDeduction { get; set; }
        public bool HasUnpaidLeave => UnpaidLeaveHours > 0;
    }
}