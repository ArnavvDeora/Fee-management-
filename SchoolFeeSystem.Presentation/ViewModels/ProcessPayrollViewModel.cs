using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ProcessPayrollViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // ✅ NEW: Search properties
        private List<Employee> _allEmployees = new List<Employee>();

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private ObservableCollection<Employee> _employees;
        [ObservableProperty] private Employee _selectedEmployee;

        [ObservableProperty] private ObservableCollection<SalarySlipItem> _salaryList;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;
        [ObservableProperty] private decimal _totalPayout;

        public ProcessPayrollViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadEmployees();
        }

        // ✅ NEW: Load all employees for search
        private void LoadEmployees()
        {
            var list = _payrollService.GetAllEmployees();
            _allEmployees = list;
            Employees = new ObservableCollection<Employee>(list);
        }

        // ✅ NEW: Search employee command
        [RelayCommand]
        public void SearchEmployee()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Show all employees if search is empty
                Employees = new ObservableCollection<Employee>(_allEmployees);
            }
            else
            {
                // Filter employees by name or biometric ID
                var filtered = _allEmployees.Where(e =>
                    (e.FullName != null && e.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.BiometricId != null && e.BiometricId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.FirstName != null && e.FirstName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.LastName != null && e.LastName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                Employees = new ObservableCollection<Employee>(filtered);
            }

            // Auto-select first result if available
            if (Employees.Count > 0)
            {
                SelectedEmployee = Employees[0];
            }
            else
            {
                SelectedEmployee = null;
            }
        }

        // ✅ NEW: Clear search
        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            SearchEmployee();
            SelectedEmployee = null;
        }

        // ✅ NEW: Calculate salary for single employee
        [RelayCommand]
        public void CalculateSingleSalary()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please search and select an employee first.", "No Employee Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var slip = _payrollService.GenerateDetailedSalary(
                SelectedEmployee.Id,
                SelectedMonth.Month,
                SelectedMonth.Year);

            if (slip != null)
            {
                SalaryList = new ObservableCollection<SalarySlipItem> { slip };
                TotalPayout = slip.NetSalary;

                MessageBox.Show(
                    $"Salary calculated for {SelectedEmployee.FullName}\n\n" +
                    $"Net Paid: ₹{slip.NetPaid:N2}\n" +
                    $"Days Worked: {slip.DaysWorked}\n" +
                    $"Gross: ₹{slip.GrossSalary:N2}",
                    "Calculation Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Error calculating salary for this employee.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CalculateAllSalaries()
        {
            var employees = _payrollService.GetAllEmployees();
            var results = new ObservableCollection<SalarySlipItem>();
            decimal total = 0;

            foreach (var emp in employees)
            {
                var slip = _payrollService.GenerateDetailedSalary(emp.Id, SelectedMonth.Month, SelectedMonth.Year);

                if (slip != null)
                {
                    results.Add(slip);
                    total += slip.NetSalary;
                }
            }

            SalaryList = results;
            TotalPayout = total;
        }

        [RelayCommand]
        public void GenerateSlip(SalarySlipItem item)
        {
            if (item == null) return;

            var services = ((App)Application.Current).Services;

            var viewerVM = services.GetRequiredService<PayslipViewerViewModel>();
            viewerVM.LoadData(item);

            var viewer = services.GetRequiredService<PayslipViewerView>();
            viewer.DataContext = viewerVM;

            var window = new Window
            {
                Title = $"Payslip - {item.Employee.FullName}",
                Content = viewer,
                Width = 850,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            window.ShowDialog();
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}