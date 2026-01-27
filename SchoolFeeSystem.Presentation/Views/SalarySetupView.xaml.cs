using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels; // Need this to see the ViewModel

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class SalarySetupView : UserControl
    {
        // Constructor Injection: The App gives us the ViewModel automatically
        public SalarySetupView(SalarySetupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; // <--- This connects the Buttons!
        }
    }
}