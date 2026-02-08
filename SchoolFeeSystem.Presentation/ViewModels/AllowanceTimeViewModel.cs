using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    /// <summary>
    /// ViewModel for viewing allowance time balances
    /// Shows employees who accrue allowance time (NOT paid for OT)
    /// </summary>
    public partial class AllowanceTimeViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty]
        private ObservableCollection<AllowanceTimeDisplay> _allowanceList;

        [ObservableProperty]
        private AllowanceTimeDisplay _selectedItem;

        [ObservableProperty]
        private string _filterDepartment = "All";

        [ObservableProperty]
        private string _searchQuery;

        // Departments that get PAID for OT (NOT shown in allowance time view)
        private static readonly string[] OT_PAID_DEPARTMENTS = {
            "TRAINING WORKSHOP",
            "CNC Workshop",
            "HEAT-TREATMENT SHOP",
            "Heat Treatment"
        };

        public AllowanceTimeViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadData();
        }

        [RelayCommand]
        public void LoadData()
        {
            var employees = _payrollService.GetAllEmployees();
            var list = new System.Collections.Generic.List<AllowanceTimeDisplay>();

            foreach (var emp in employees)
            {
                // ✅ FIXED: Skip OT-paid departments (they get cash, not allowance time)
                if (IsOTPaidDepartment(emp.Department))
                    continue;

                var allowance = _payrollService.GetOvertimeAllowance(emp.Id);

                list.Add(new AllowanceTimeDisplay
                {
                    EmployeeId = emp.Id,
                    EmployeeName = emp.FullName,
                    Department = emp.Department ?? "N/A",
                    BiometricId = emp.BiometricId ?? "-",
                    TotalHours = Math.Round(allowance.TotalAllowanceMinutes / 60.0, 2),
                    UsedHours = Math.Round(allowance.UsedAllowanceMinutes / 60.0, 2),
                    AvailableHours = Math.Round(allowance.AvailableMinutes / 60.0, 2),
                    LastUpdated = allowance.LastUpdated.ToString("dd MMM yyyy HH:mm"),

                    // Additional info
                    TotalMinutes = allowance.TotalAllowanceMinutes,
                    UsedMinutes = allowance.UsedAllowanceMinutes,
                    AvailableMinutes = allowance.AvailableMinutes
                });
            }

            // Sort by available hours (descending)
            list = list.OrderByDescending(x => x.AvailableHours).ToList();

            AllowanceList = new ObservableCollection<AllowanceTimeDisplay>(list);
        }

        [RelayCommand]
        public void SearchFilter()
        {
            LoadData();

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var filtered = AllowanceList.Where(x =>
                    x.EmployeeName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    x.Department.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    x.BiometricId.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                AllowanceList = new ObservableCollection<AllowanceTimeDisplay>(filtered);
            }
        }

        [RelayCommand]
        public void ExportToCsv()
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"AllowanceTime_{DateTime.Now:yyyyMMdd}.csv",
                Filter = "CSV Files (*.csv)|*.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var csv = new System.Text.StringBuilder();
                csv.AppendLine("Employee Name,Department,BioID,Total Hours,Used Hours,Available Hours,Last Updated");

                foreach (var item in AllowanceList)
                {
                    csv.AppendLine($"{item.EmployeeName},{item.Department},{item.BiometricId}," +
                                 $"{item.TotalHours},{item.UsedHours},{item.AvailableHours},{item.LastUpdated}");
                }

                System.IO.File.WriteAllText(saveDialog.FileName, csv.ToString());
                MessageBox.Show("Export successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        /// <summary>
        /// Check if department gets PAID for overtime (these are excluded from allowance time view)
        /// </summary>
        private bool IsOTPaidDepartment(string department)
        {
            if (string.IsNullOrEmpty(department)) return false;

            return OT_PAID_DEPARTMENTS.Any(d =>
                department.Equals(d, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Display model for allowance time grid
    /// </summary>
    public class AllowanceTimeDisplay
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public string BiometricId { get; set; }
        public double TotalHours { get; set; }
        public double UsedHours { get; set; }
        public double AvailableHours { get; set; }
        public string LastUpdated { get; set; }

        // Internal tracking
        public int TotalMinutes { get; set; }
        public int UsedMinutes { get; set; }
        public int AvailableMinutes { get; set; }

        // Display helpers
        public string TotalDisplay => $"{TotalHours:F2} hrs ({TotalMinutes} min)";
        public string UsedDisplay => $"{UsedHours:F2} hrs ({UsedMinutes} min)";
        public string AvailableDisplay => $"{AvailableHours:F2} hrs ({AvailableMinutes} min)";

        // Status indicator
        public string StatusColor => AvailableHours > 10 ? "Green" :
                                     AvailableHours > 5 ? "Orange" : "Red";
    }
}