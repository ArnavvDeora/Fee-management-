using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using SchoolFeeSystem.Presentation; // <--- CRITICAL: Allows access to (App) class

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AttendanceManagementViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private ObservableCollection<Employee> _employees;
        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;
        [ObservableProperty] private ObservableCollection<AttendanceRecord> _attendanceRecords;
        [ObservableProperty] private string _statusMessage = "Ready";

        public AttendanceManagementViewModel(IAttendanceService attendanceService, IPayrollService payrollService)
        {
            _attendanceService = attendanceService;
            _payrollService = payrollService;
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            var list = _payrollService.GetAllEmployees();
            if (list.Count > 0)
            {
                Employees = new ObservableCollection<Employee>(list);
                SelectedEmployee = Employees[0];
            }
        }

        [RelayCommand]
        public void LoadAttendance()
        {
            if (SelectedEmployee == null) return;
            var data = _attendanceService.GetRecords(SelectedEmployee.Id, SelectedMonth.Month, SelectedMonth.Year);
            AttendanceRecords = new ObservableCollection<AttendanceRecord>(data);
        }
        [RelayCommand]
        public void ImportExcel()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Attendance Reports|*.csv;*.xlsx;*.xls",
                Title = "Select Biometric Report"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _attendanceService.ImportBiometricReport(openFileDialog.FileName);

                    // --- AUTO-REFRESH FIX ---
                    // 1. Read the filename or content to guess the date, 
                    // OR just set SelectedMonth to the most recent record in DB

                    // For now, let's inform the user explicitly:
                    MessageBox.Show("Attendance Imported!\n\nCheck the Month/Year in the file (e.g., Nov 2024) and select that month in the date picker above to see the data.",
                                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Force Reload
                    LoadAttendance();

                    // Optional: You could query the DB for the latest record date and set SelectedMonth = that date.
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            // FIX: Explicitly cast Application.Current to (App) to access Services
            var services = ((App)Application.Current).Services;

            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // Opens the popup for manual edits
        [RelayCommand]
        public void EditRecord(AttendanceRecord record)
        {
            if (record == null) return;

            var services = ((App)Application.Current).Services;
            var editVM = services.GetRequiredService<EditAttendanceViewModel>();
            editVM.SetRecord(record);

            var editWindow = new EditAttendanceWindow();
            editWindow.DataContext = editVM;

            if (editWindow.ShowDialog() == true)
            {
                LoadAttendance();
            }
        }
    }
}