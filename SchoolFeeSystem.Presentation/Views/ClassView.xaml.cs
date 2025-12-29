using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ClassView : UserControl
    {
        public ClassView(ClassViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}