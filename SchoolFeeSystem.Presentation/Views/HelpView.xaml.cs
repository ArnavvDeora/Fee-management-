using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class HelpView : UserControl
    {
        public HelpView(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}