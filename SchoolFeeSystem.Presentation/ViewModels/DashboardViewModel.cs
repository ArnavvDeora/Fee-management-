using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using Microsoft.Extensions.DependencyInjection; // Need this for getting services
using SchoolFeeSystem.Presentation.Views;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentView;

        public DashboardViewModel()
        {
            CurrentView = "Welcome! Select an option from the left.";
        }

        [RelayCommand]

        public void ShowStudents()
        {
            // Switch the main view to StudentView
            CurrentView = App.Current.Services.GetRequiredService<StudentView>();
        }
        [RelayCommand]
        public void ShowFees()
        {
            CurrentView = App.Current.Services.GetRequiredService<FeeView>();
        }
        [RelayCommand]
        public void ShowFeeCollection()
        {
            CurrentView = App.Current.Services.GetRequiredService<FeeCollectionView>();
        }
        [RelayCommand]
        public void ShowReports()
        {
            CurrentView = App.Current.Services.GetRequiredService<ReportsView>();
        }
        [RelayCommand]
        public void ShowClasses()
        {
            CurrentView = App.Current.Services.GetRequiredService<ClassView>();
        }
        [RelayCommand]
        public void ShowHelp()
        {
            CurrentView = App.Current.Services.GetRequiredService<HelpView>();
        }
        [RelayCommand]
        public void Logout()
        {
            var loginWindow = App.Current.Services.GetRequiredService<LoginView>();
            loginWindow.Show();

            if (Application.Current.Windows.Count > 0)
            {
                Application.Current.Windows[0]?.Close();
            }
        }
    }
}