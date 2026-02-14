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
                // ✅ FIX: Properly resize and center window BEFORE showing content
                if (Application.Current.MainWindow != null)
                {
                    var window = Application.Current.MainWindow;

                    // First, switch to normal state
                    window.WindowState = WindowState.Normal;

                    // Enable resizing for main app
                    window.ResizeMode = ResizeMode.CanResize;

                    // Resize to main app dimensions
                    window.Width = 1100;
                    window.Height = 700;

                    // ✅ CENTER THE WINDOW after resizing
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                    // Force re-center (WindowStartupLocation only works on first show)
                    window.Left = (SystemParameters.PrimaryScreenWidth - window.Width) / 2;
                    window.Top = (SystemParameters.PrimaryScreenHeight - window.Height) / 2;

                    // Update title
                    window.Title = "School Management System";

                    // NOW navigate to Main Selection View
                    var selectionScreen = App.Current.Services.GetRequiredService<MainSelectionView>();
                    window.Content = selectionScreen;
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