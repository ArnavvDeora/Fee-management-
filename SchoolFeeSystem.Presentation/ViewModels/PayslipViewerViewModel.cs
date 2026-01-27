using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using System.Windows.Controls;
using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class PayslipViewerViewModel : ObservableObject
    {
        // We now store the entire detailed slip, not just name/lists
        [ObservableProperty]
        private SalarySlipItem _currentSlip;

        // Called by ProcessPayrollViewModel to inject the data
        public void LoadData(SalarySlipItem slip)
        {
            CurrentSlip = slip;
        }

        // The Command now takes the Visual (The Grid/Border) from the View
        [RelayCommand]
        public void PrintSlip(Visual visual)
        {
            if (visual == null) return;

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                // This opens the Windows Print dialog (Select "Microsoft Print to PDF" here)
                printDialog.PrintVisual(visual, $"Payslip - {CurrentSlip?.Employee?.FullName ?? "Employee"}");
            }
        }
    }
}