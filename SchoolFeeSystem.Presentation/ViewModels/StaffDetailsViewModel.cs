using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StaffDetailsViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private bool _isEditMode = false;

        // For Increment Popup logic (simplified)
        [ObservableProperty] private bool _isIncrementVisible = false;
        [ObservableProperty] private decimal _incrementAmount;

        public StaffDetailsViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        // Called when the page loads to set the specific person
        public void SetEmployee(Employee emp)
        {
            SelectedEmployee = emp;
            IsEditMode = false;
            IsIncrementVisible = false;
        }

        [RelayCommand]
        public void ToggleEditMode()
        {
            if (IsEditMode)
            {
                // Save Changes
                _payrollService.UpdateEmployee(SelectedEmployee);
                MessageBox.Show("Details updated successfully!");
                IsEditMode = false;
            }
            else
            {
                // Enter Edit Mode
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
            if (IncrementAmount > 0)
            {
                SelectedEmployee.BaseSalary += IncrementAmount;
                _payrollService.UpdateEmployee(SelectedEmployee);

                MessageBox.Show($"Salary increased by ₹{IncrementAmount}. New Salary: ₹{SelectedEmployee.BaseSalary}");

                IncrementAmount = 0;
                IsIncrementVisible = false;
                OnPropertyChanged(nameof(SelectedEmployee)); // Refresh UI
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var directory = App.Current.Services.GetRequiredService<StaffDirectoryView>();
            // Force refresh of the list
            directory.DataContext = App.Current.Services.GetRequiredService<StaffDirectoryViewModel>();
            Application.Current.MainWindow.Content = directory;
        }
    }
}