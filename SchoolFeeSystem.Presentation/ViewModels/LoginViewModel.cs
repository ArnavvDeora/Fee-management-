using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Interfaces;
using System.Windows;
using SchoolFeeSystem.Presentation.Views; // Needed for DashboardView

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

        public void Login(string password)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter username and password.");
                return;
            }

            var user = _authService.Login(Username, password);
            if (user != null)
            {
                // 1. Get the Dashboard Window using our new App.Services
                var dashboard = App.Current.Services.GetService(typeof(DashboardView)) as Window;

                if (dashboard != null)
                {
                    dashboard.Show();

                    // 2. Close the Login Window (which is currently the main window)
                    // We check if Windows[0] exists to avoid crashes
                    if (Application.Current.Windows.Count > 0)
                    {
                        Application.Current.Windows[0].Close();
                    }
                }
            }
            else
            {
                MessageBox.Show("Invalid credentials.");
            }
        }
    }
}