using System.Windows.Controls;
using System.Windows;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ClassView : UserControl
    {
        public ClassView(ClassViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // -----------------------------------------------------------------------
        // AUTO-GENERATING COLUMN — hide internal columns the user should not see:
        //   _Section  : internal grouping tag added by CsvDataService
        //   Sr No.    : the raw (often empty) cell from the Excel file
        //   Sr No     : alternate spelling
        // The visible Sr No. is provided by LoadingRow below (1-based row index).
        // -----------------------------------------------------------------------
        private void StudentDataGrid_AutoGeneratingColumn(object sender,
            DataGridAutoGeneratingColumnEventArgs e)
        {
            string colName = e.Column.Header?.ToString() ?? "";

            // Hide the internal _Section tag column
            if (colName.StartsWith("_"))
            {
                e.Cancel = true;
                return;
            }

            // Hide the raw Sr No. cell from Excel (it is usually empty or wrong).
            // We will show a clean auto-numbered column via LoadingRow instead.
            if (colName.Equals("Sr No.", System.StringComparison.OrdinalIgnoreCase) ||
                colName.Equals("Sr No", System.StringComparison.OrdinalIgnoreCase) ||
                colName.Equals("Sr.", System.StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                return;
            }
        }

        // -----------------------------------------------------------------------
        // LOADING ROW — fires for every row as it becomes visible.
        // We use the Row.Header to display a clean 1-based serial number.
        // The Header sits in the row-header gutter to the left of the data columns.
        // -----------------------------------------------------------------------
        private void StudentDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // +1 because GetIndex() is 0-based
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}