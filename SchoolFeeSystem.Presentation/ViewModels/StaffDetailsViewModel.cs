using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32; // Required for OpenFileDialog
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

        public StaffDetailsViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        // Called when the page loads to set the specific person
        public void SetEmployee(Employee emp)
        {
            // [IMPROVEMENT] Reload from DB to ensure we have the latest data (like Photo)
            // If the employee was just imported, the object passed might be incomplete.
            var freshData = _payrollService.GetEmployeeWithSalaryDetails(emp.Id);
            SelectedEmployee = freshData ?? emp;

            IsEditMode = false;
            IsIncrementVisible = false;
            IncrementAmount = 0;
        }

        // ---------------------------------------------------------
        // 1. [NEW] PHOTO UPLOAD FEATURE
        // ---------------------------------------------------------
        [RelayCommand]
        public void BrowsePhoto()
        {
            // Only allow changing photo if we are in "Edit Mode"
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
                    // 1. Read file into bytes
                    byte[] photoBytes = File.ReadAllBytes(dlg.FileName);

                    // 2. Assign to Employee Object
                    SelectedEmployee.Photo = photoBytes;

                    // 3. Notify UI to refresh the image immediately
                    OnPropertyChanged(nameof(SelectedEmployee));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------------
        // 2. EDIT & SAVE LOGIC
        // ---------------------------------------------------------
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

        // ---------------------------------------------------------
        // 3. INCREMENT LOGIC
        // ---------------------------------------------------------
        [RelayCommand]
        public void ShowIncrement()
        {
            IsIncrementVisible = !IsIncrementVisible;
        }

        [RelayCommand]
        public void ApplyIncrement()
        {
            if (IncrementAmount > 0)
            {
                try
                {
                    // Update object
                    SelectedEmployee.BaseSalary += IncrementAmount;

                    // Save to DB (using generic update or specific salary config if available)
                    _payrollService.UpdateEmployee(SelectedEmployee);

                    // Optional: Log history if your service supports it
                    // _payrollService.SaveSalaryConfiguration(SelectedEmployee, "Increment Applied");

                    MessageBox.Show($"Salary increased by ₹{IncrementAmount}. New Salary: ₹{SelectedEmployee.BaseSalary}");

                    IncrementAmount = 0;
                    IsIncrementVisible = false;
                    OnPropertyChanged(nameof(SelectedEmployee)); // Refresh UI
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error applying increment: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Please enter a valid amount greater than 0.");
            }
        }

        // ---------------------------------------------------------
        // 4. NAVIGATION
        // ---------------------------------------------------------
        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;

            var directoryView = services.GetRequiredService<StaffDirectoryView>();
            var directoryVM = services.GetRequiredService<StaffDirectoryViewModel>();

            // [IMPORTANT] Force refresh the list so the new Photo/Name appears immediately
            directoryVM.RefreshData();

            directoryView.DataContext = directoryVM;
            Application.Current.MainWindow.Content = directoryView;
        }
    }
}