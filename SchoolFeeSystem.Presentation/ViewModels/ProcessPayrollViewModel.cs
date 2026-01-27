using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection; // For getting Services
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ProcessPayrollViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private ObservableCollection<SalarySlipItem> _salaryList;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;
        [ObservableProperty] private decimal _totalPayout;

        public ProcessPayrollViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        [RelayCommand]
        public void CalculateAllSalaries()
        {
            var employees = _payrollService.GetAllEmployees();
            var results = new ObservableCollection<SalarySlipItem>();
            decimal total = 0;

            foreach (var emp in employees)
            {
                // CALL THE NEW LOGIC HERE
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

            // Open the Printable Slip Window
            var services = ((App)Application.Current).Services;

            // Get the View Model and Load Data
            var viewerVM = services.GetRequiredService<PayslipViewerViewModel>();
            viewerVM.LoadData(item); // Pass the detailed item

            // Open Window
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