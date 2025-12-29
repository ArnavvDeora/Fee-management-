using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class FeeCollectionView : UserControl
    {
        public FeeCollectionView(FeeCollectionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}