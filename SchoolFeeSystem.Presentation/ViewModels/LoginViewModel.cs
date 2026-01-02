using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Interfaces;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Windows.Controls; // Needed for PasswordBox

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

        // --- THE MISSING LINE WAS HERE! ---
        [RelayCommand]
        public void Login(object parameter)
        {
            // Safely get the password from the box
            var passwordBox = parameter as PasswordBox;
            var password = passwordBox?.Password;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter username and password.");
                return;
            }

            var user = _authService.Login(Username, password);
            if (user != null)
            {
                // 1. Get the Main Selection View (The Hub)
                var selectionScreen = App.Current.Services.GetRequiredService<MainSelectionView>();

                // 2. Swap the content
                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.Content = selectionScreen;

                    // 3. Resize for the main app
                    Application.Current.MainWindow.Width = 1100;
                    Application.Current.MainWindow.Height = 700;
                    Application.Current.MainWindow.WindowState = WindowState.Normal;
                    Application.Current.MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    Application.Current.MainWindow.Title = "School Management System";
                }
            }
            else
            {
                MessageBox.Show("Invalid credentials.");
            }
        }
    }
}