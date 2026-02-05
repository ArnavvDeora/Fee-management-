using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels; 

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class AttendanceManagementView : UserControl
    {
        public AttendanceManagementView(AttendanceManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            System.Diagnostics.Debug.WriteLine($"DataContext set: {DataContext != null}");
            System.Diagnostics.Debug.WriteLine($"DataContext type: {DataContext?.GetType().Name}");
        }
    }
}