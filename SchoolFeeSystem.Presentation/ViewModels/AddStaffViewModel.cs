using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AddStaffViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // Form Fields
        [ObservableProperty] private string _firstName;
        [ObservableProperty] private string _lastName;
        [ObservableProperty] private string _designation;
        [ObservableProperty] private string _department;
        [ObservableProperty] private string _email;
        [ObservableProperty] private string _phone;
        [ObservableProperty] private decimal _baseSalary;

        // "Teaching" or "Non-Teaching"
        [ObservableProperty] private string _staffType = "Teaching";

        public AddStaffViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        [RelayCommand]
        public void SaveEmployee()
        {
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(Designation))
            {
                MessageBox.Show("Please enter at least a First Name and Designation.");
                return;
            }

            var newEmp = new Employee
            {
                FirstName = FirstName,
                LastName = LastName,
                Designation = Designation,
                Department = Department,
                Email = Email,
                PhoneNumber = Phone,
                BaseSalary = BaseSalary,
                StaffType = StaffType,
                IsActive = true
            };

            _payrollService.AddEmployee(newEmp);
            MessageBox.Show("Staff member added successfully!");
            GoBack();
        }

        [RelayCommand]
        public void GoBack()
        {
            var directory = App.Current.Services.GetRequiredService<StaffDirectoryView>();
            Application.Current.MainWindow.Content = directory;
        }
    }
}