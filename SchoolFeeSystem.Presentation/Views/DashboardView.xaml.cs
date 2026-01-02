using SchoolFeeSystem.Presentation.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView(DashboardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}