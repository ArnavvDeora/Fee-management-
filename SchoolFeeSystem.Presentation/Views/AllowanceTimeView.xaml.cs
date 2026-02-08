using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    /// <summary>
    /// Interaction logic for AllowanceTimeView.xaml
    /// </summary>
    public partial class AllowanceTimeView : UserControl
    {
        public AllowanceTimeView(AllowanceTimeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
