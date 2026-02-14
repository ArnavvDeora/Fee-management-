using SchoolFeeSystem.Core.Entities;
using System;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface ICompanyGatePassService
    {
        /// <summary>
        /// Get or create gate pass for employee for current month
        /// </summary>
        CompanyGatePass GetOrCreateGatePass(int employeeId, int month, int year);

        /// <summary>
        /// Try to use company gate pass for time deduction
        /// Returns minutes deducted from gate pass
        /// </summary>
        int TryUseGatePass(int employeeId, int minutesNeeded, string reason, DateTime useDate);

        /// <summary>
        /// Check if gate pass can be used
        /// </summary>
        bool CanUseGatePass(int employeeId, int month, int year);

        /// <summary>
        /// Get remaining gate pass time for employee
        /// </summary>
        int GetRemainingMinutes(int employeeId, int month, int year);

        /// <summary>
        /// Get number of uses remaining for month
        /// </summary>
        int GetRemainingUses(int employeeId, int month, int year);

        /// <summary>
        /// Reset all gate passes for a new month (run on 1st of each month)
        /// </summary>
        void ResetMonthlyGatePasses(int month, int year);

        /// <summary>
        /// Get gate pass statistics for employee
        /// </summary>
        GatePassStatistics GetGatePassStatistics(int employeeId, int month, int year);
    }
    public class MonthlyGatePassResetJob
    {
        private readonly ICompanyGatePassService _gatePassService;

        public void Execute()
        {
            int month = DateTime.Now.Month;
            int year = DateTime.Now.Year;

            _gatePassService.ResetMonthlyGatePasses(month, year);

            Console.WriteLine($"✅ Gate passes reset for {month}/{year}");
        }
    }
    /// <summary>
    /// Gate pass usage statistics
    /// </summary>
    public class GatePassStatistics
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public int TotalAllowanceMinutes { get; set; }
        public int UsedMinutes { get; set; }
        public int RemainingMinutes { get; set; }
        public int TimesUsed { get; set; }
        public int RemainingUses { get; set; }
        public bool IsExhausted { get; set; }
        public string Status { get; set; }
    }
}