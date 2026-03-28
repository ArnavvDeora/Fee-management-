using System.Windows;
using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class StudentListView : UserControl
    {
        public StudentListView(StudentListViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // -----------------------------------------------------------------------
        // AUTO-GENERATING COLUMN
        // Hides raw/internal columns. Clean Sr No. comes from LoadingRow (below).
        // Hidden:
        //   _Section      — internal grouping tag from CsvDataService
        //   Sr No. / Sr No / Sr. — raw serial from Excel (often empty)
        // -----------------------------------------------------------------------
        private void StudentGrid_AutoGeneratingColumn(object sender,
            DataGridAutoGeneratingColumnEventArgs e)
        {
            string col = e.Column.Header?.ToString() ?? "";

            // 1. Hide internal CsvDataService columns
            if (col.StartsWith("_"))
            {
                e.Cancel = true;
                return;
            }

            // 2. Hide the raw Sr No. cell from Excel
            if (col.Equals("Sr No.", System.StringComparison.OrdinalIgnoreCase) ||
                col.Equals("Sr No", System.StringComparison.OrdinalIgnoreCase) ||
                col.Equals("Sr.", System.StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                return;
            }

            // 3. Style key money columns
            string lower = col.ToLower();
            if (lower.Contains("pending") || lower.Contains("fee") || lower.Contains("paid") ||
                lower.Contains("fine") || lower.Contains("amount"))
            {
                // Right-align numeric columns
                if (e.Column is DataGridTextColumn dtc)
                {
                    dtc.ElementStyle = new System.Windows.Style(typeof(TextBlock))
                    {
                        Setters =
                        {
                            new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right),
                            new Setter(TextBlock.PaddingProperty, new Thickness(0, 0, 12, 0))
                        }
                    };
                }
            }

            // 4. Limit column width for very wide columns (remarks, notes, etc.)
            if (lower.Contains("remark") || lower.Contains("note"))
            {
                e.Column.MaxWidth = 200;
            }
        }

        // -----------------------------------------------------------------------
        // LOADING ROW — fires for every visible row.
        // We show a clean 1-based serial number in the row-header gutter.
        // -----------------------------------------------------------------------
        private void StudentGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        // -----------------------------------------------------------------------
        // SELECTION CHANGED — expand row details for the selected student.
        // Collapses the previously-selected row so only one detail panel shows.
        // -----------------------------------------------------------------------
        private void StudentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                // Collapse all rows first
                foreach (var item in dg.Items)
                {
                    var row = dg.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                    if (row != null)
                        row.DetailsVisibility = Visibility.Collapsed;
                }

                // Expand only the selected row
                if (dg.SelectedItem != null)
                {
                    var selectedRow = dg.ItemContainerGenerator
                        .ContainerFromItem(dg.SelectedItem) as DataGridRow;
                    if (selectedRow != null)
                        selectedRow.DetailsVisibility = Visibility.Visible;
                }
            }
        }

        // -----------------------------------------------------------------------
        // TREE VIEW SELECTION — forwards the selected tree node to the ViewModel.
        // -----------------------------------------------------------------------
        private void TreeView_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is StudentListViewModel vm)
                vm.OnTreeSelectionChanged(e.NewValue);
        }
    }
}