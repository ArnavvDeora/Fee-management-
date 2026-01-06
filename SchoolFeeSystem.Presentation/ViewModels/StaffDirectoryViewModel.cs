using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Collections.Generic;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StaffDirectoryViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // Search Bar Text
        [ObservableProperty]
        private string _searchText;

        // The list displayed in the "Teaching" tab
        [ObservableProperty]
        private ObservableCollection<Employee> _teachingStaff;

        // The list displayed in the "Non-Teaching" tab
        [ObservableProperty]
        private ObservableCollection<Employee> _nonTeachingStaff;

        public StaffDirectoryViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadData();
        }

        // Triggered when SearchText changes or "Search" button is clicked
        [RelayCommand]
        public void PerformSearch()
        {
            LoadData();
        }

        private void LoadData()
        {
            // Fetch both lists based on the search text
            var teachers = _payrollService.SearchStaff(SearchText, "Teaching");
            var nonTeachers = _payrollService.SearchStaff(SearchText, "Non-Teaching");

            TeachingStaff = new ObservableCollection<Employee>(teachers);
            NonTeachingStaff = new ObservableCollection<Employee>(nonTeachers);
        }

        // --- FIXED: Single Definition for AddNewStaff ---
        [RelayCommand]
        public void AddNewStaff()
        {
            // Navigate to the "Add Staff" View
            var addView = App.Current.Services.GetRequiredService<AddStaffView>();
            Application.Current.MainWindow.Content = addView;
        }

        // --- FIXED: Single Definition for ViewDetails ---
        [RelayCommand]
        public void ViewDetails(Employee employee)
        {
            if (employee == null) return;

            // 1. Get the details ViewModel
            var detailsVM = App.Current.Services.GetRequiredService<StaffDetailsViewModel>();

            // 2. Load the specific employee data
            detailsVM.SetEmployee(employee);

            // 3. Get the View and inject the populated ViewModel
            var detailsView = App.Current.Services.GetRequiredService<StaffDetailsView>();
            detailsView.DataContext = detailsVM;

            // 4. Navigate
            Application.Current.MainWindow.Content = detailsView;
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}