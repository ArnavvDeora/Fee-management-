using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentView;

        public DashboardViewModel()
        {
            // Default view
            CurrentView = App.Current.Services.GetRequiredService<StudentView>();
        }

        // --- Navigation Commands ---

        [RelayCommand]
        public void ShowStudents() => CurrentView = App.Current.Services.GetRequiredService<StudentView>();

        [RelayCommand]
        public void ShowFees() => CurrentView = App.Current.Services.GetRequiredService<FeeView>();

        [RelayCommand]
        public void ShowFeeCollection() => CurrentView = App.Current.Services.GetRequiredService<FeeCollectionView>();

        [RelayCommand]
        public void ShowReports() => CurrentView = App.Current.Services.GetRequiredService<ReportsView>();

        [RelayCommand]
        public void ShowClasses() => CurrentView = App.Current.Services.GetRequiredService<ClassView>();

        [RelayCommand]
        public void ShowHelp() => CurrentView = App.Current.Services.GetRequiredService<HelpView>();

        // --- NEW: Back Button Logic ---
        [RelayCommand]
        public void GoBack()
        {
            // Navigate back to the Hub
            var selectionScreen = App.Current.Services.GetRequiredService<MainSelectionView>();
            Application.Current.MainWindow.Content = selectionScreen;
        }

        // --- FIXED: Logout Logic ---
        [RelayCommand]
        public void Logout()
        {
            // Get the Login UserControl
            var loginScreen = App.Current.Services.GetRequiredService<LoginView>();

            // Set it as the main content (Swapping instead of closing windows)
            Application.Current.MainWindow.Content = loginScreen;

            // Resize window to look like a Login screen
            Application.Current.MainWindow.Width = 450;
            Application.Current.MainWindow.Height = 550;
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Title = "Login";
        }
    }
}