using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ReportsView : UserControl
    {
        public ReportsView(ReportsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // Hide internal columns (_Section, raw Sr No.)
        private void DataGrid_AutoGeneratingColumn(object sender,
            DataGridAutoGeneratingColumnEventArgs e)
        {
            string col = e.Column.Header?.ToString() ?? "";
            if (col.StartsWith("_")) { e.Cancel = true; return; }
            if (col.Equals("Sr No.", System.StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; return; }
            if (col.Equals("Sr No", System.StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; return; }
            if (col.Equals("Sr.", System.StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; return; }
        }

        // 1-based row number in the row-header gutter
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}