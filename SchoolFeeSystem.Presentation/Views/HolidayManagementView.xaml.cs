using SchoolFeeSystem.Presentation.ViewModels;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class HolidayManagementView : UserControl
    {
        public HolidayManagementView(HolidayManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; 
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}