using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection; // Add this
using SchoolFeeSystem.Presentation.Views;
using System.Windows; // Add this

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class MainSelectionViewModel : ObservableObject
    {
        [RelayCommand]
        public void OpenFeeManagement()
        {
            // Navigate to the existing Student Dashboard
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            App.Current.MainWindow.Content = dashboard;
        }

        [RelayCommand]
        public void OpenPayrollManagement()
        {
            // Navigate to the NEW Payroll Dashboard (We will build this next)
            var payrollDash = App.Current.Services.GetRequiredService<PayrollDashboardView>();
            App.Current.MainWindow.Content = payrollDash;
        }
    }
}