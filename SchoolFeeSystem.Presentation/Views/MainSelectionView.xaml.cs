using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class MainSelectionView : UserControl
    {
        public MainSelectionView(MainSelectionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}