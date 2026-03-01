using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AttendanceManagementViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;
        private readonly IPayrollService _payrollService;

        private List<Employee> _allEmployees = new();
        private List<AttendanceRecord> _allAttendanceRecords = new(); // master list for filtering

        [ObservableProperty] private string _searchText;
        [ObservableProperty] private ObservableCollection<Employee> _employees;
        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;
        [ObservableProperty] private ObservableCollection<AttendanceRecord> _attendanceRecords;
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isImporting = false;
        [ObservableProperty] private string _importProgress = "";

        // ── Department filter ────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _departments = new();
        [ObservableProperty] private string _selectedDepartment = "All Departments";

        // Auto-refresh grid when department selection changes
        partial void OnSelectedDepartmentChanged(string value) => ApplyDepartmentFilter();

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
            LoadDepartments();
        }

        private void LoadDepartments()
        {
            var deptList = _allEmployees
                .Select(e => e.Department ?? "Unknown")
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            deptList.Insert(0, "All Departments");
            Departments = new ObservableCollection<string>(deptList);
            SelectedDepartment = "All Departments";
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
            _allAttendanceRecords = data.ToList();
            ApplyDepartmentFilter();
        }

        // ── Department filter ────────────────────────────────────────────────
        private void ApplyDepartmentFilter()
        {
            if (_allAttendanceRecords == null) return;

            if (string.IsNullOrEmpty(SelectedDepartment) || SelectedDepartment == "All Departments")
            {
                AttendanceRecords = new ObservableCollection<AttendanceRecord>(_allAttendanceRecords);
            }
            else
            {
                var filtered = _allAttendanceRecords
                    .Where(r => r.Employee?.Department != null &&
                                r.Employee.Department.Equals(SelectedDepartment, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                AttendanceRecords = new ObservableCollection<AttendanceRecord>(filtered);
            }
        }

        [RelayCommand]
        public void ClearDepartmentFilter()
        {
            SelectedDepartment = "All Departments"; // triggers OnSelectedDepartmentChanged → ApplyDepartmentFilter
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

        // =========================================================
        // ⚠️ DEV ONLY — RESET ALL ATTENDANCE & ALLOWANCE DATA
        // Hide this button before handing over to the company.
        // =========================================================
        [RelayCommand]
        public async Task ResetAllData()
        {
            var confirm = MessageBox.Show(
                "⚠️ WARNING: This will permanently delete ALL attendance records\n" +
                "and reset ALL overtime allowance balances to zero.\n\n" +
                "This CANNOT be undone!\n\nAre you absolutely sure?",
                "DEV RESET — Are you sure?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // Second confirmation
            var confirm2 = MessageBox.Show(
                "Last chance! Click Yes to wipe all data.",
                "Final Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm2 != MessageBoxResult.Yes) return;

            try
            {
                IsImporting = true;
                StatusMessage = "Resetting all data...";

                var progress = new Progress<string>(msg =>
                {
                    ImportProgress = msg;
                    StatusMessage = msg;
                });

                await Task.Run(() => _attendanceService.ResetAllAttendanceAndAllowances(progress));

                StatusMessage = "✅ Reset complete. Re-import your attendance files now.";
                MessageBox.Show(
                    "All attendance records and allowance balances have been cleared.\n\nYou can now re-import attendance files fresh.",
                    "Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadAttendance();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Reset failed: {ex.Message}";
                MessageBox.Show($"Reset failed:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsImporting = false;
                ImportProgress = "";
            }
        }
    }
}