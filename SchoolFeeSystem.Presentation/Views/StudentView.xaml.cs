using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class StudentView : UserControl
    {
        public StudentView(StudentViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}