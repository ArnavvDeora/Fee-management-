using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ProcessPayrollView : UserControl
    {
        public ProcessPayrollView(ProcessPayrollViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}