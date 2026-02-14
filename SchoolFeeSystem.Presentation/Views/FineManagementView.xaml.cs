using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class FineManagementView : UserControl
    {
        public FineManagementView(FineManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}