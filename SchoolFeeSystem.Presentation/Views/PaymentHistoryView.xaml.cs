using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class PaymentHistoryView : UserControl
    {
        private readonly PaymentHistoryViewModel _viewModel;

        public PaymentHistoryView(PaymentHistoryViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            Loaded += OnViewLoaded;

            // Tab toggle-button mutual-exclusion (needs no DataContext).
            BtnTabHistory.Checked += (_, _) => BtnTabSummary.IsChecked = false;
            BtnTabSummary.Checked += (_, _) => BtnTabHistory.IsChecked = false;
            BtnTabHistory.Unchecked += (s, _) =>
            {
                if (s is ToggleButton tb && BtnTabSummary.IsChecked != true) tb.IsChecked = true;
            };
            BtnTabSummary.Unchecked += (s, _) =>
            {
                if (s is ToggleButton tb && BtnTabHistory.IsChecked != true) tb.IsChecked = true;
            };
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnViewLoaded;

            // DispatcherPriority.ContextIdle fires after ALL layout/render/binding
            // passes complete — the safest moment to touch ItemsSource.
            Dispatcher.InvokeAsync(InitializeView, DispatcherPriority.ContextIdle);
        }

        private void InitializeView()
        {
            // Null out ItemsSource FIRST so ItemCollection.IsUsingItemsSource is
            // false before we call Clear(). Calling Items.Clear() while ItemsSource
            // is non-null throws InvalidOperationException (line 1160 in
            // ItemCollection.cs: "Operation is not valid while ItemsSource is in use").
            PaymentHistoryGrid.ItemsSource = null;
            FinancialSummaryGrid.ItemsSource = null;

            // Now safe to clear the internal item stores.
            PaymentHistoryGrid.Items.Clear();
            FinancialSummaryGrid.Items.Clear();

            // Resolve all XAML bindings (TextBlocks, Buttons, ComboBox, etc.)
            DataContext = _viewModel;

            // Load data — PropertyChanged fires but we are not subscribed yet.
            _viewModel.Initialize();

            // Wire ItemsSource directly — bypasses DataBindEngine entirely.
            PaymentHistoryGrid.ItemsSource = _viewModel.PaymentHistoryView;
            FinancialSummaryGrid.ItemsSource = _viewModel.FinancialSummaryView;

            // Subscribe NOW so future Search / Filter / Refresh / Clear updates flow through.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PaymentHistoryViewModel.PaymentHistoryView):
                    // Always null ItemsSource before clearing — this is the root
                    // cause of the crash you saw. The DataGrid's internal
                    // ItemCollection checks IsUsingItemsSource and throws if you
                    // call Clear() while a source is attached.
                    PaymentHistoryGrid.ItemsSource = null;
                    PaymentHistoryGrid.Items.Clear();
                    PaymentHistoryGrid.ItemsSource = _viewModel.PaymentHistoryView;
                    break;

                case nameof(PaymentHistoryViewModel.FinancialSummaryView):
                    FinancialSummaryGrid.ItemsSource = null;
                    FinancialSummaryGrid.Items.Clear();
                    FinancialSummaryGrid.ItemsSource = _viewModel.FinancialSummaryView;
                    break;
            }
        }
    }
}