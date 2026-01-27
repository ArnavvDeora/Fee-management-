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

        [ObservableProperty] private string _searchText;
        [ObservableProperty] private ObservableCollection<Employee> _teachingStaff;
        [ObservableProperty] private ObservableCollection<Employee> _nonTeachingStaff;

        public StaffDirectoryViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadData();
        }

        // --- THIS WAS MISSING ---
        // Public method allows other pages to force a refresh
        public void RefreshData()
        {
            LoadData();
        }

        [RelayCommand]
        public void PerformSearch()
        {
            LoadData();
        }

        private void LoadData()
        {
            // Fetch fresh data from database
            // Note: Ensure your IPayrollService has SearchStaff. If not, replace with GetAllEmployees logic.
            var teachers = _payrollService.SearchStaff(SearchText, "Teaching");
            var nonTeachers = _payrollService.SearchStaff(SearchText, "Non-Teaching");

            TeachingStaff = new ObservableCollection<Employee>(teachers);
            NonTeachingStaff = new ObservableCollection<Employee>(nonTeachers);
        }

        [RelayCommand]
        public void AddNewStaff()
        {
            var addView = ((App)Application.Current).Services.GetRequiredService<AddStaffView>();
            Application.Current.MainWindow.Content = addView;
        }

        [RelayCommand]
        public void ViewDetails(Employee employee)
        {
            if (employee == null) return;
            var services = ((App)Application.Current).Services;

            var detailsVM = services.GetRequiredService<StaffDetailsViewModel>();
            detailsVM.SetEmployee(employee);

            var detailsView = services.GetRequiredService<StaffDetailsView>();
            detailsView.DataContext = detailsVM;
            Application.Current.MainWindow.Content = detailsView;
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