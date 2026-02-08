using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Diagnostics;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class HelpViewModel : ObservableObject
    {
        [ObservableProperty]
        private string supportEmail = "support@schoolfeesystem.com";

        [ObservableProperty]
        private string supportPhone = "+91-9876543210";

        [RelayCommand]
        public void SendEmail()
        {
            try
            {
                string mailtoUrl = $"mailto:{SupportEmail}?subject=Fee Management System Support Request";
                Process.Start(new ProcessStartInfo
                {
                    FileName = mailtoUrl,
                    UseShellExecute = true
                });

                MessageBox.Show(
                    "Your default email client has been opened.\n\nPlease send your query and we will get back to you soon!",
                    "Email Client Opened",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open email client: {ex.Message}\n\nPlease email us manually at: {SupportEmail}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CopyNumber()
        {
            try
            {
                Clipboard.SetText(SupportPhone);
                MessageBox.Show(
                    $"Phone number copied to clipboard!\n\n{SupportPhone}",
                    "Copied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Failed to copy number: {ex.Message}\n\nPhone: {SupportPhone}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void GoBack3()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}