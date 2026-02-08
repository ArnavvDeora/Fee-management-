using System.Windows;
using System.Windows.Controls;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class LeaveManagementView : UserControl
    {
        // ✅ FIXED: No automatic DataContext injection
        public LeaveManagementView()
        {
            InitializeComponent();
            // DataContext will be set manually
        }

        private void CalculateCustomHours_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as ViewModels.LeaveManagementViewModel;
            viewModel?.CalculateCustomHoursCommand.Execute(null);
        }
    }
}