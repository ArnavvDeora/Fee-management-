using SchoolFeeSystem.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class AddStaffView : UserControl
    {
        public AddStaffView(AddStaffViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Regex to allow ONLY numbers (rejects letters/symbols)
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}