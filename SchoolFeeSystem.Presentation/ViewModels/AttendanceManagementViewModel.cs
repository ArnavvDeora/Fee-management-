using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AttendanceManagementViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;
        private readonly IPayrollService _payrollService;

        private List<Employee> _allEmployees = new();

        [ObservableProperty] private string _searchText;
        [ObservableProperty] private ObservableCollection<Employee> _employees;
        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;
        [ObservableProperty] private ObservableCollection<AttendanceRecord> _attendanceRecords;
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isImporting = false;
        [ObservableProperty] private string _importProgress = "";

        // ===================================================================
        // MANUALLY IMPLEMENTED COMMANDS (Source generator not working)
        // ===================================================================
        private IAsyncRelayCommand _importExcelAsyncCommand;

        public IAsyncRelayCommand ImportExcelAsyncCommand =>
            _importExcelAsyncCommand ??= new AsyncRelayCommand(ImportExcelAsyncExecute);

        public AttendanceManagementViewModel(IAttendanceService attendanceService, IPayrollService payrollService)
        {
            _attendanceService = attendanceService;
            _payrollService = payrollService;
            LoadEmployees();

            // DIAGNOSTIC
            System.Diagnostics.Debug.WriteLine("=== VIEWMODEL INITIALIZED ===");
            System.Diagnostics.Debug.WriteLine($"ImportExcelAsyncCommand created: {ImportExcelAsyncCommand != null}");
        }

        private void LoadEmployees()
        {
            var list = _payrollService.GetAllEmployees();
            _allEmployees = list;
            if (list.Count > 0)
            {
                Employees = new ObservableCollection<Employee>(list);
                SelectedEmployee = Employees[0];
            }
        }

        [RelayCommand]
        public void SearchEmployee()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Employees = new ObservableCollection<Employee>(_allEmployees);
            }
            else
            {
                var filtered = _allEmployees.Where(e =>
                    (e.FullName != null && e.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.BiometricId != null && e.BiometricId.Contains(SearchText))
                ).ToList();
                Employees = new ObservableCollection<Employee>(filtered);
            }

            if (Employees.Count > 0)
            {
                SelectedEmployee = Employees[0];
                LoadAttendance();
            }
        }

        [RelayCommand]
        public void LoadAttendance()
        {
            if (SelectedEmployee == null) return;
            var data = _attendanceService.GetRecords(SelectedEmployee.Id, SelectedMonth.Month, SelectedMonth.Year);
            AttendanceRecords = new ObservableCollection<AttendanceRecord>(data);
        }

        // =========================================================
        // IMPORT EXCEL ASYNC - MANUAL IMPLEMENTATION (NOT USING [RelayCommand])
        // =========================================================
        private async Task ImportExcelAsyncExecute()
        {
            System.Diagnostics.Debug.WriteLine("=== IMPORT BUTTON CLICKED ===");

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Attendance Reports|*.csv;*.xlsx;*.xls",
                Title = "Select Attendance Report"
            };

            System.Diagnostics.Debug.WriteLine("Opening file dialog...");

            if (openFileDialog.ShowDialog() != true)
            {
                System.Diagnostics.Debug.WriteLine("Dialog cancelled");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"File selected: {openFileDialog.FileName}");

            try
            {
                IsImporting = true;
                StatusMessage = "Starting import...";
                ImportProgress = "Initializing...";

                string filePath = openFileDialog.FileName;

                var progress = new Progress<string>(msg =>
                {
                    ImportProgress = msg;
                    StatusMessage = msg;
                    System.Diagnostics.Debug.WriteLine($"Progress: {msg}");
                });

                // Call the ASYNC method
                await _attendanceService.ImportAttendanceAsync(filePath, progress);

                // Success!
                StatusMessage = "✅ Import completed successfully!";
                System.Diagnostics.Debug.WriteLine("Import SUCCESS!");

                MessageBox.Show(
                    "Attendance data imported successfully!",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // Refresh the view
                LoadAttendance();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Import failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Import ERROR: {ex.Message}");

                MessageBox.Show(
                    $"Import Error:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsImporting = false;
                ImportProgress = "";
                System.Diagnostics.Debug.WriteLine("Import finished");
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        [RelayCommand]
        public void EditRecord(AttendanceRecord record)
        {
            if (record == null) return;
            var services = ((App)Application.Current).Services;
            var editVM = services.GetRequiredService<EditAttendanceViewModel>();
            editVM.SetRecord(record);
            var editWindow = new EditAttendanceWindow();
            editWindow.DataContext = editVM;
            if (editWindow.ShowDialog() == true) LoadAttendance();
        }
    }
}