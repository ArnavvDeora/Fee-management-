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

            // Defer everything to after ALL layout/render passes complete.
            // DispatcherPriority.ContextIdle fires only when the message queue
            // is fully drained — guaranteed after every binding, layout, and
            // render pass. This is later than Loaded, Render, or DataBind.
            Dispatcher.InvokeAsync(InitializeView, DispatcherPriority.ContextIdle);
        }

        private void InitializeView()
        {
            // Explicitly clear both DataGrids before touching ItemsSource.
            // This ensures ItemCollection.IsUsingItemsSource == false and
            // _internalView is null, so SetItemsSource never throws.
            PaymentHistoryGrid.Items.Clear();
            FinancialSummaryGrid.Items.Clear();

            // Set DataContext — all XAML bindings (TextBlocks, Buttons,
            // ComboBox) resolve now. DataGrids have no ItemsSource binding
            // in XAML so they are unaffected.
            DataContext = _viewModel;

            // Load data — PropertyChanged fires but we are not subscribed yet.
            _viewModel.Initialize();

            // Set ItemsSource directly in code — bypasses DataBindEngine.
            PaymentHistoryGrid.ItemsSource = _viewModel.PaymentHistoryView;
            FinancialSummaryGrid.ItemsSource = _viewModel.FinancialSummaryView;

            // Subscribe NOW so future Search/Filter/Refresh updates flow through.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PaymentHistoryViewModel.PaymentHistoryView):
                    PaymentHistoryGrid.Items.Clear();
                    PaymentHistoryGrid.ItemsSource = _viewModel.PaymentHistoryView;
                    break;

                case nameof(PaymentHistoryViewModel.FinancialSummaryView):
                    FinancialSummaryGrid.Items.Clear();
                    FinancialSummaryGrid.ItemsSource = _viewModel.FinancialSummaryView;
                    break;
            }
        }
    }
}