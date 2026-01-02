using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class HelpViewModel : ObservableObject
    {
        // Your Details
        public string SupportEmail { get; } = "arnavdeora@gmail.com";
        public string SupportPhone { get; } = "+91 7973966694";

        [RelayCommand]
        public void SendEmail()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = $"mailto:{SupportEmail}?subject=Support Request - School Fee System",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                MessageBox.Show($"Could not open mail app. Please manually email: {SupportEmail}");
            }
        }

        [RelayCommand]
        public void CopyNumber()
        {
            Clipboard.SetText(SupportPhone);
            MessageBox.Show("Phone number copied to clipboard!");
        }
    }
}