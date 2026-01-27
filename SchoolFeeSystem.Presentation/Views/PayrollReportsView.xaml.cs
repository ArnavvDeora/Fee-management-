using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class PayrollReportsView : UserControl
    {
        public PayrollReportsView(PayrollReportsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

