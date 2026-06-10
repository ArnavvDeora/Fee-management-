using SchoolFeeSystem.Presentation.ViewModels;
using System.Windows.Controls;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class HolidayManagementView : UserControl
    {
        public HolidayManagementView(HolidayManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}