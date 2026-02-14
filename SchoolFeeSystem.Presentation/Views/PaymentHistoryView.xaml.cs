using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class PaymentHistoryView : UserControl
    {
        public PaymentHistoryView(PaymentHistoryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}