using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Windows;

// FIX: This namespace allows access to the 'App' class
using SchoolFeeSystem;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class PayrollDashboardViewModel : ObservableObject
    {
        [RelayCommand]
        public void GoToManageStaff()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<StaffDirectoryView>();
            var vm = services.GetRequiredService<StaffDirectoryViewModel>();

            // FIX: Now this works because we added the public method in Step 1
            vm.RefreshData();

            view.DataContext = vm;
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoToSalarySetup()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<SalarySetupView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoToAttendance()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<AttendanceManagementView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoToHolidays()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<HolidayManagementView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoToPayrollReports()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<PayrollReportsView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoToProcessPayroll()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<ProcessPayrollView>();
            Application.Current.MainWindow.Content = view;
        }
    }
}