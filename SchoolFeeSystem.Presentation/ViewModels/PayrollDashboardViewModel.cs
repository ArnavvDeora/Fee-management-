using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // DTOs for View
    public class SalaryTrendData
    {
        public string Month { get; set; } = string.Empty;
        public double ValueScale { get; set; }
        public string FormattedLabel { get; set; } = string.Empty;
    }

    public class RecentPaySlipDto
    {
        public string EmpId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public partial class PayrollDashboardViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // --- Stats ---
        [ObservableProperty] private int _totalEmployees;
        [ObservableProperty] private decimal _totalMonthlyPayout;
        [ObservableProperty] private int _pendingSalariesCount;
        [ObservableProperty] private int _paidSalariesCount;

        // --- Lists ---
        [ObservableProperty] private ObservableCollection<SalaryTrendData> _salaryTrend = new();
        [ObservableProperty] private ObservableCollection<string> _recentActivities = new();
        [ObservableProperty] private ObservableCollection<RecentPaySlipDto> _recentPaySlips = new();

        public PayrollDashboardViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadRealData();
        }

        private void LoadRealData()
        {
            // 1. Determine Current Month (e.g., "Jan-2026")
            string currentMonth = DateTime.Now.ToString("MMM-yyyy");

            // 2. Fetch Top Card Stats
            TotalEmployees = _payrollService.GetTotalEmployees();
            TotalMonthlyPayout = _payrollService.GetTotalPayoutForMonth(currentMonth);
            PendingSalariesCount = _payrollService.GetPendingCount(currentMonth);
            PaidSalariesCount = _payrollService.GetPaidCount(currentMonth);

            // 3. Generate "Recent Activity" Log
            var activities = new List<(DateTime Date, string Message)>();

            // A) Add Recent Payments
            var recentPayments = _payrollService.GetRecentPaidSalaries(5);
            foreach (var pay in recentPayments)
            {
                string msg = $"Salary Paid to {pay.Employee.FullName} for {pay.MonthYear}";
                // Use PaymentDate if available, else Today
                DateTime date = pay.PaymentDate ?? DateTime.Now;
                activities.Add((date, msg));
            }

            // B) Add New Employees
            var newHires = _payrollService.GetRecentEmployees(3);
            foreach (var emp in newHires)
            {
                string msg = $"New Staff: {emp.FullName} joined as {emp.Designation}";
                activities.Add((emp.JoiningDate, msg));
            }

            // Sort by Date (Newest first) and show in list
            var sortedActivities = activities.OrderByDescending(x => x.Date).Select(x => x.Message).ToList();
            RecentActivities = new ObservableCollection<string>(sortedActivities);

            // 4. Fetch Recent Pay Slips (Grid)
            // reusing the recent payments logic for the grid
            var slips = new List<RecentPaySlipDto>();
            foreach (var pay in recentPayments)
            {
                slips.Add(new RecentPaySlipDto
                {
                    EmpId = $"E{pay.EmployeeId:000}",
                    Name = pay.Employee.FullName,
                    Month = pay.MonthYear,
                    Amount = pay.FinalAmount,
                    Status = pay.Status
                });
            }
            RecentPaySlips = new ObservableCollection<RecentPaySlipDto>(slips);

            // 5. Generate Chart Data (Last 4 Months)
            LoadChartData();
        }

        private void LoadChartData()
        {
            var history = _payrollService.GetPayoutHistory(4);
            var chartData = new List<SalaryTrendData>();

            // Find Max value to calculate scale (0.0 to 1.0)
            decimal maxVal = history.Count > 0 ? history.Values.Max() : 1;
            if (maxVal == 0) maxVal = 1;

            foreach (var item in history)
            {
                chartData.Add(new SalaryTrendData
                {
                    Month = item.Key.Split('-')[0], // "Jan-2026" -> "Jan"
                    ValueScale = (double)(item.Value / maxVal),
                    FormattedLabel = $"₹{(item.Value / 100000):0.0}L" // Formats as Lacs
                });
            }
            SalaryTrend = new ObservableCollection<SalaryTrendData>(chartData);
        }

        // --- Navigation ---
        [RelayCommand]
        public void GoBack()
        {
            var selectionScreen = App.Current.Services.GetRequiredService<MainSelectionView>();
            Application.Current.MainWindow.Content = selectionScreen;
        }

        [RelayCommand]
        public void Logout()
        {
            var loginScreen = App.Current.Services.GetRequiredService<LoginView>();
            Application.Current.MainWindow.Content = loginScreen;
            Application.Current.MainWindow.Width = 450;
            Application.Current.MainWindow.Height = 550;
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Title = "Login";
        }
    }
}