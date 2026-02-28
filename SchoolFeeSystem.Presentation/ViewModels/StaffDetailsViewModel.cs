using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.IO;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StaffDetailsViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private bool _isEditMode = false;

        // For Increment Popup logic
        [ObservableProperty] private bool _isIncrementVisible = false;
        [ObservableProperty] private decimal _incrementAmount;

        // ✅ For Delete Confirmation
        [ObservableProperty] private bool _isDeleteConfirmVisible = false;

        public StaffDetailsViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        public void SetEmployee(Employee emp)
        {
            var freshData = _payrollService.GetEmployeeWithSalaryDetails(emp.Id);
            SelectedEmployee = freshData ?? emp;

            IsEditMode = false;
            IsIncrementVisible = false;
            IncrementAmount = 0;
            IsDeleteConfirmVisible = false;
        }

        [RelayCommand]
        public void BrowsePhoto()
        {
            if (!IsEditMode) return;

            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Update Staff Photo"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    byte[] photoBytes = File.ReadAllBytes(dlg.FileName);
                    SelectedEmployee.Photo = photoBytes;
                    OnPropertyChanged(nameof(SelectedEmployee));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message);
                }
            }
        }

        [RelayCommand]
        public void ToggleEditMode()
        {
            if (IsEditMode)
            {
                // User clicked "Save Changes"
                try
                {
                    if (string.IsNullOrWhiteSpace(SelectedEmployee.FirstName) || string.IsNullOrWhiteSpace(SelectedEmployee.PhoneNumber))
                    {
                        MessageBox.Show("First Name and Mobile Number are required.");
                        return;
                    }

                    _payrollService.UpdateEmployee(SelectedEmployee);
                    MessageBox.Show("Details updated successfully!");
                    IsEditMode = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving data: {ex.Message}");
                }
            }
            else
            {
                // User clicked "Edit Profile"
                IsEditMode = true;
            }
        }

        [RelayCommand]
        public void ShowIncrement()
        {
            IsIncrementVisible = !IsIncrementVisible;
        }

        [RelayCommand]
        public void ApplyIncrement()
        {
            if (IncrementAmount <= 0)
            {
                MessageBox.Show("Please enter a valid amount greater than 0.");
                return;
            }

            decimal oldSalary = SelectedEmployee.BaseSalary;

            try
            {
                SelectedEmployee.BaseSalary += IncrementAmount;

                // SaveSalaryConfiguration does TWO things in one call:
                //   1. _context.Employees.Update(employee) + SaveChanges()  → persists new BaseSalary to DB
                //   2. Inserts a SalaryRevision row                         → payroll history & audit trail
                //
                // GenerateDetailedSalary() reads emp.BaseSalary directly from the Employees table,
                // so all future payroll calculations automatically pick up the new salary.
                _payrollService.SaveSalaryConfiguration(
                    SelectedEmployee,
                    $"Increment ₹{IncrementAmount:N0} applied. " +
                    $"Previous: ₹{oldSalary:N0} → New: ₹{SelectedEmployee.BaseSalary:N0}");

                MessageBox.Show(
                    $"✅ Salary Increment Applied!\n\n" +
                    $"Employee  : {SelectedEmployee.FullName}\n" +
                    $"Old Salary: ₹{oldSalary:N2}\n" +
                    $"Increment : +₹{IncrementAmount:N2}\n" +
                    $"New Salary: ₹{SelectedEmployee.BaseSalary:N2}\n\n" +
                    $"Change saved to database. All future payroll runs will use ₹{SelectedEmployee.BaseSalary:N2}.",
                    "Increment Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                IncrementAmount = 0;
                IsIncrementVisible = false;
                OnPropertyChanged(nameof(SelectedEmployee));
            }
            catch (Exception ex)
            {
                // Roll back the in-memory change so the UI doesn't show a wrong value
                SelectedEmployee.BaseSalary = oldSalary;
                OnPropertyChanged(nameof(SelectedEmployee));
                MessageBox.Show($"Error applying increment:\n{ex.Message}");
            }
        }

        // ✅ Show Delete Confirmation
        [RelayCommand]
        public void ShowDeleteConfirmation()
        {
            if (!IsEditMode)
            {
                MessageBox.Show(
                    "Please enter Edit Mode first before deleting an employee.",
                    "Edit Mode Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            IsDeleteConfirmVisible = true;
        }

        // ✅ Cancel Delete
        [RelayCommand]
        public void CancelDelete()
        {
            IsDeleteConfirmVisible = false;
        }

        // ✅ Confirm and Delete Employee - PROPER IMPLEMENTATION
        [RelayCommand]
        public void ConfirmDelete()
        {
            if (SelectedEmployee == null) return;

            // ===== FIRST CONFIRMATION =====
            var firstConfirm = MessageBox.Show(
                $"⚠️ FIRST CONFIRMATION ⚠️\n\n" +
                $"Are you ABSOLUTELY SURE you want to delete this employee?\n\n" +
                $"Employee: {SelectedEmployee.FullName}\n" +
                $"ID: {SelectedEmployee.BiometricId}\n" +
                $"Designation: {SelectedEmployee.Designation}\n" +
                $"Department: {SelectedEmployee.Department}\n\n" +
                $"This will PERMANENTLY delete ALL data including:\n" +
                $"• Employee record\n" +
                $"• All attendance history\n" +
                $"• All leave records\n" +
                $"• All salary history\n" +
                $"• All allowances and deductions\n\n" +
                $"This action CANNOT be undone!",
                "Delete Employee - First Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (firstConfirm != MessageBoxResult.Yes)
            {
                IsDeleteConfirmVisible = false;
                return;
            }

            // ===== SECOND CONFIRMATION =====
            var secondConfirm = MessageBox.Show(
                $"⚠️⚠️ FINAL CONFIRMATION ⚠️⚠️\n\n" +
                $"LAST CHANCE TO CANCEL!\n\n" +
                $"This will PERMANENTLY DELETE:\n\n" +
                $"✗ Employee Record: {SelectedEmployee.FullName}\n" +
                $"✗ All {CountEstimatedRecords()} Attendance Records\n" +
                $"✗ All Leave Requests\n" +
                $"✗ All Salary History & Revisions\n" +
                $"✗ All Allowances & Deductions\n" +
                $"✗ All Overtime/Allowance Balance\n" +
                $"✗ All Company Gate Pass Usage\n" +
                $"✗ ALL Related Data\n\n" +
                $"Type YES to confirm deletion:",
                "Delete Employee - FINAL Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);

            if (secondConfirm != MessageBoxResult.Yes)
            {
                IsDeleteConfirmVisible = false;
                return;
            }

            // ===== PROCEED WITH PERMANENT DELETION =====
            try
            {
                string employeeName = SelectedEmployee.FullName;
                string employeeId = SelectedEmployee.BiometricId ?? "N/A";
                int empId = SelectedEmployee.Id;

                // ✅ Call the PROPER delete method from PayrollService
                bool success = _payrollService.DeleteEmployeePermanently(empId);

                if (success)
                {
                    MessageBox.Show(
                        $"✅ DELETION COMPLETE\n\n" +
                        $"Employee '{employeeName}' (ID: {employeeId}) and ALL associated data " +
                        $"have been permanently deleted from the database.\n\n" +
                        $"Deleted data includes:\n" +
                        $"✓ Employee profile\n" +
                        $"✓ Attendance records\n" +
                        $"✓ Leave history\n" +
                        $"✓ Salary revisions\n" +
                        $"✓ Allowances & deductions\n" +
                        $"✓ All related data\n\n" +
                        $"This action cannot be reversed.",
                        "Employee Permanently Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Navigate back to directory
                    GoBack();
                }
                else
                {
                    MessageBox.Show(
                        $"❌ DELETION FAILED\n\n" +
                        $"Could not delete employee '{employeeName}'.\n\n" +
                        $"Possible reasons:\n" +
                        $"• Employee doesn't exist\n" +
                        $"• Database error\n" +
                        $"• Permission denied\n\n" +
                        $"Please check the logs and try again.",
                        "Deletion Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ ERROR DURING DELETION\n\n" +
                    $"An unexpected error occurred while deleting the employee:\n\n" +
                    $"{ex.Message}\n\n" +
                    $"The employee data may still be in the database.\n" +
                    $"Please contact your system administrator.",
                    "Deletion Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"Deletion error: {ex}");
            }
            finally
            {
                IsDeleteConfirmVisible = false;
            }
        }

        // ✅ Helper method to estimate record count for user information
        private string CountEstimatedRecords()
        {
            try
            {
                // This is just for display purposes in the confirmation dialog
                // You could make it more accurate by actually querying the database
                int monthsSinceJoining = ((DateTime.Now.Year - SelectedEmployee.JoiningDate.Year) * 12) +
                                        (DateTime.Now.Month - SelectedEmployee.JoiningDate.Month);

                int estimatedAttendance = monthsSinceJoining * 22; // ~22 working days per month

                return estimatedAttendance > 0 ? $"~{estimatedAttendance}" : "0";
            }
            catch
            {
                return "multiple";
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;

            var directoryView = services.GetRequiredService<StaffDirectoryView>();
            var directoryVM = services.GetRequiredService<StaffDirectoryViewModel>();

            // Refresh the directory to ensure deleted employee doesn't appear
            directoryVM.RefreshData();

            directoryView.DataContext = directoryVM;
            Application.Current.MainWindow.Content = directoryView;
        }
    }
}