using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Interfaces;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Windows.Controls;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        private string _username = string.Empty;

        public LoginViewModel(IAuthService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        public void Login(object parameter)
        {
            // Safely get the password from the PasswordBox parameter
            var passwordBox = parameter as PasswordBox;
            var password = passwordBox?.Password;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Please enter username and password.",
                    "Login Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var user = _authService.Login(Username, password);
            if (user != null)
            {
                // Successfully logged in - navigate to Main Selection View
                var selectionScreen = App.Current.Services.GetRequiredService<MainSelectionView>();

                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.Content = selectionScreen;

                    // Resize window for the main application
                    Application.Current.MainWindow.Width = 1100;
                    Application.Current.MainWindow.Height = 700;
                    Application.Current.MainWindow.WindowState = WindowState.Normal;
                    Application.Current.MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    Application.Current.MainWindow.Title = "School Management System";
                }

                MessageBox.Show(
                    $"Welcome, {user.Username}!",
                    "Login Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Invalid username or password. Please try again.",
                    "Login Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}