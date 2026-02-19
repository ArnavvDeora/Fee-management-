using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class PaymentHistoryView : UserControl
    {
        public PaymentHistoryView(PaymentHistoryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Make the two ToggleButtons behave like a RadioButton group:
            // clicking one automatically unchecks the other.
            // This is done in code-behind because RadioButton inside a
            // custom ControlTemplate loses its GroupName behaviour.
            BtnTabHistory.Checked += (_, _) => BtnTabSummary.IsChecked = false;
            BtnTabSummary.Checked += (_, _) => BtnTabHistory.IsChecked = false;

            // Prevent either button from being un-checked by clicking it again
            // (at least one tab must always be active).
            BtnTabHistory.Unchecked += (s, _) => { if (s is ToggleButton tb && BtnTabSummary.IsChecked != true) tb.IsChecked = true; };
            BtnTabSummary.Unchecked += (s, _) => { if (s is ToggleButton tb && BtnTabHistory.IsChecked != true) tb.IsChecked = true; };
        }
    }
}