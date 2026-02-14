using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class CompanyGatePassService : ICompanyGatePassService
    {
        private readonly AppDbContext _context;

        public CompanyGatePassService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get or create gate pass for employee for specific month.
        /// ✅ SAFE: Wrapped in try-catch to handle DB not ready edge cases.
        /// </summary>
        public CompanyGatePass GetOrCreateGatePass(int employeeId, int month, int year)
        {
            try
            {
                var gatePass = _context.CompanyGatePasses
                    .FirstOrDefault(g => g.EmployeeId == employeeId &&
                                        g.Month == month &&
                                        g.Year == year);

                if (gatePass == null)
                {
                    gatePass = new CompanyGatePass
                    {
                        EmployeeId = employeeId,
                        Month = month,
                        Year = year,
                        TotalAllowanceMinutes = 120,
                        UsedMinutes = 0,
                        TimesUsed = 0,
                        MaxUsesPerMonth = 2,
                        CreatedOn = DateTime.Now
                    };

                    _context.CompanyGatePasses.Add(gatePass);
                    _context.SaveChanges();
                }

                return gatePass;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GetOrCreateGatePass error: {ex.Message}");

                // Return a default in-memory gate pass so UI doesn't crash
                return new CompanyGatePass
                {
                    EmployeeId = employeeId,
                    Month = month,
                    Year = year,
                    TotalAllowanceMinutes = 120,
                    UsedMinutes = 0,
                    TimesUsed = 0,
                    MaxUsesPerMonth = 2,
                    CreatedOn = DateTime.Now
                };
            }
        }

        /// <summary>
        /// Try to use gate pass. Company Gate Pass is ALWAYS used FIRST.
        /// Max 2 uses per month regardless of time remaining.
        /// </summary>
        public int TryUseGatePass(int employeeId, int minutesNeeded, string reason, DateTime useDate)
        {
            if (minutesNeeded <= 0) return 0;

            try
            {
                var gatePass = GetOrCreateGatePass(employeeId, useDate.Month, useDate.Year);

                if (!gatePass.CanUse)
                    return 0;

                int minutesToDeduct = Math.Min(minutesNeeded, gatePass.RemainingMinutes);

                // Only update if this is a persisted entity (has Id > 0)
                if (gatePass.Id > 0)
                {
                    gatePass.UsedMinutes += minutesToDeduct;
                    gatePass.TimesUsed += 1;
                    gatePass.LastUsedOn = DateTime.Now;
                    _context.CompanyGatePasses.Update(gatePass);
                    _context.SaveChanges();
                }

                return minutesToDeduct;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ TryUseGatePass error: {ex.Message}");
                return 0;
            }
        }

        public bool CanUseGatePass(int employeeId, int month, int year)
        {
            try
            {
                var gatePass = GetOrCreateGatePass(employeeId, month, year);
                return gatePass.CanUse;
            }
            catch { return false; }
        }

        public int GetRemainingMinutes(int employeeId, int month, int year)
        {
            try
            {
                var gatePass = GetOrCreateGatePass(employeeId, month, year);
                return gatePass.RemainingMinutes;
            }
            catch { return 0; }
        }

        public int GetRemainingUses(int employeeId, int month, int year)
        {
            try
            {
                var gatePass = GetOrCreateGatePass(employeeId, month, year);
                return Math.Max(0, gatePass.MaxUsesPerMonth - gatePass.TimesUsed);
            }
            catch { return 0; }
        }

        public void ResetMonthlyGatePasses(int month, int year)
        {
            try
            {
                var employees = _context.Employees.Where(e => e.IsActive).ToList();
                foreach (var emp in employees)
                    GetOrCreateGatePass(emp.Id, month, year);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ ResetMonthlyGatePasses error: {ex.Message}");
            }
        }

        public GatePassStatistics GetGatePassStatistics(int employeeId, int month, int year)
        {
            try
            {
                var employee = _context.Employees.Find(employeeId);
                var gatePass = GetOrCreateGatePass(employeeId, month, year);

                return new GatePassStatistics
                {
                    EmployeeId = employeeId,
                    EmployeeName = employee?.FullName ?? "Unknown",
                    Month = month,
                    Year = year,
                    TotalAllowanceMinutes = gatePass.TotalAllowanceMinutes,
                    UsedMinutes = gatePass.UsedMinutes,
                    RemainingMinutes = gatePass.RemainingMinutes,
                    TimesUsed = gatePass.TimesUsed,
                    RemainingUses = Math.Max(0, gatePass.MaxUsesPerMonth - gatePass.TimesUsed),
                    IsExhausted = gatePass.IsExhausted,
                    Status = gatePass.Status
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GetGatePassStatistics error: {ex.Message}");

                // Return a safe default so UI doesn't crash
                return new GatePassStatistics
                {
                    EmployeeId = employeeId,
                    EmployeeName = "Unknown",
                    Month = month,
                    Year = year,
                    TotalAllowanceMinutes = 120,
                    UsedMinutes = 0,
                    RemainingMinutes = 120,
                    TimesUsed = 0,
                    RemainingUses = 2,
                    IsExhausted = false,
                    Status = "Available"
                };
            }
        }
    }
}