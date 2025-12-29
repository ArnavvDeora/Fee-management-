using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class FeeView : UserControl
    {
        public FeeView(FeeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}