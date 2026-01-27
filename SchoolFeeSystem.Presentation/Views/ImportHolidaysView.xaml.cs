using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ImportHolidaysView : UserControl
    {
        public ImportHolidaysView(ImportHolidaysViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}