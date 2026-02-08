using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ClassViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;

        // Store original data to avoid cross-page filter issues
        private DataTable _originalData;
        private string _currentSheetName; // Store actual sheet name (not display name)

        // ==========================
        // SHEET HANDLING
        // ==========================

        public ObservableCollection<string> SheetNames { get; } = new();

        [ObservableProperty]
        private string selectedSheet;

        [ObservableProperty]
        private string sheetSearchText;

        // ==========================
        // TABLE DATA
        // ==========================

        [ObservableProperty]
        private DataView csvTableView;

        // ==========================
        // SEARCH
        // ==========================

        [ObservableProperty]
        private string searchText;

        // ==========================
        // ROW SELECTION
        // ==========================

        [ObservableProperty]
        private DataRowView selectedRow;

        // ==========================
        // UPDATE CELL
        // ==========================

        public ObservableCollection<string> Columns { get; } = new();

        [ObservableProperty]
        private string selectedColumn;

        [ObservableProperty]
        private string newValue;

        // Filtered sheet names for dropdown search
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        // ==========================
        // CONSTRUCTOR
        // ==========================

        public ClassViewModel(CsvDataService csvService)
        {
            _csvService = csvService;
            LoadSheets();
        }

        // ==========================
        // LOAD SHEETS
        // ==========================

        private void LoadSheets()
        {
            SheetNames.Clear();
            FilteredSheetNames.Clear();

            // Use display names that include the time period
            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }

            if (SheetNames.Count > 0)
                SelectedSheet = SheetNames[0];
        }

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();

            if (string.IsNullOrWhiteSpace(value))
            {
                foreach (var name in SheetNames)
                    FilteredSheetNames.Add(name);
            }
            else
            {
                foreach (var name in SheetNames)
                {
                    if (name.ToLower().Contains(value.ToLower()))
                        FilteredSheetNames.Add(name);
                }
            }
        }

        partial void OnSelectedSheetChanged(string value)
        {
            // Clear search when changing sheets
            SearchText = string.Empty;
            LoadSheetData(value);
        }

        private void LoadSheetData(string displayName)
        {
            // Convert display name to actual sheet name
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);

            var table = _csvService.GetSheet(_currentSheetName);
            if (table == null)
                return;

            // Use the SAME table instance, don't copy
            _originalData = table;
            CsvTableView = _originalData.DefaultView;
            CsvTableView.RowFilter = string.Empty; // Ensure no filter is applied

            Columns.Clear();
            foreach (DataColumn col in _originalData.Columns)
                Columns.Add(col.ColumnName);
        }

        // ==========================
        // SEARCH
        // ==========================

        [RelayCommand]
        public void Search()
        {
            if (_originalData == null) return;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                CsvTableView.RowFilter = string.Empty;
                return;
            }

            try
            {
                // Create a new filtered DataTable
                var filtered = _originalData.Clone();

                foreach (DataRow row in _originalData.Rows)
                {
                    foreach (var item in row.ItemArray)
                    {
                        if (item != null && item.ToString().Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Rows.Add(row.ItemArray);
                            break;
                        }
                    }
                }

                CsvTableView = filtered.DefaultView;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Search failed: {ex.Message}\n\nPlease try a different search term.",
                    "Search Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            if (_originalData != null)
            {
                CsvTableView = _originalData.DefaultView;
                CsvTableView.RowFilter = string.Empty;
            }
        }

        // ==========================
        // ROW ACTIONS
        // ==========================

        [RelayCommand]
        public void AddRow()
        {
            if (_originalData == null)
            {
                MessageBox.Show("No table loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newRow = _originalData.NewRow();

            // Fill with empty strings (to avoid null issues)
            foreach (DataColumn col in _originalData.Columns)
                newRow[col.ColumnName] = "";

            // Auto-populate serial number if it's the first column
            if (_originalData.Columns.Count > 0)
            {
                var firstCol = _originalData.Columns[0];
                if (firstCol.ColumnName.ToLower().Contains("sr") || firstCol.ColumnName.ToLower().Contains("no"))
                {
                    int nextNumber = _originalData.Rows.Count + 1;
                    newRow[firstCol.ColumnName] = nextNumber.ToString();
                }
            }

            if (SelectedRow == null)
            {
                // If nothing selected → add at end
                _originalData.Rows.Add(newRow);
            }
            else
            {
                // Find the row in the original data
                var selectedRowData = SelectedRow.Row;
                int index = _originalData.Rows.IndexOf(selectedRowData);

                if (index >= 0)
                {
                    _originalData.Rows.InsertAt(newRow, index + 1);
                }
                else
                {
                    _originalData.Rows.Add(newRow);
                }
            }

            // Refresh view
            CsvTableView = _originalData.DefaultView;

            MessageBox.Show(
                "New row added successfully!",
                "Row Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void DeleteSelectedRow()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Please select a row to delete.", "No Row Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to delete this row?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Find and delete from original data
                    var rowToDelete = SelectedRow.Row;

                    foreach (DataRow row in _originalData.Rows)
                    {
                        bool match = true;
                        for (int i = 0; i < _originalData.Columns.Count; i++)
                        {
                            if (!row[i].Equals(rowToDelete[i]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            _originalData.Rows.Remove(row);
                            break;
                        }
                    }

                    // Refresh view
                    CsvTableView = _originalData.DefaultView;

                    MessageBox.Show(
                        "Row deleted successfully!",
                        "Row Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to delete row: {ex.Message}",
                        "Delete Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        // ==========================
        // UPDATE CELL
        // ==========================

        [RelayCommand]
        public void UpdateCell()
        {
            if (SelectedRow == null || string.IsNullOrWhiteSpace(SelectedColumn))
            {
                MessageBox.Show(
                    "Please select a row and a column first.",
                    "Selection Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Update in the original data
                var rowToUpdate = SelectedRow.Row;

                foreach (DataRow row in _originalData.Rows)
                {
                    bool match = true;
                    for (int i = 0; i < _originalData.Columns.Count; i++)
                    {
                        if (!row[i].Equals(rowToUpdate[i]))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        row[SelectedColumn] = NewValue;

                        // CRITICAL FIX: Recalculate fees after updating
                        _csvService.RecalculateRowFees(_currentSheetName, row);
                        break;
                    }
                }

                // Refresh view
                CsvTableView = _originalData.DefaultView;

                MessageBox.Show(
                    $"Cell updated successfully!\n\nColumn: {SelectedColumn}\nNew Value: {NewValue}",
                    "Cell Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Clear input
                NewValue = string.Empty;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update cell: {ex.Message}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================
        // SAVE CHANGES
        // ==========================

        [RelayCommand]
        public void SaveChanges()
        {
            try
            {
                _csvService.SaveFile();
                MessageBox.Show(
                    "✅ Changes saved successfully!",
                    "Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to save changes:\n\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================
        // BACK BUTTON
        // ==========================

        [RelayCommand]
        public void GoBack()
        {
            // Check if there are unsaved changes
            var result = MessageBox.Show(
                "Do you want to save changes before going back?",
                "Save Changes?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _csvService.SaveFile();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to save: {ex.Message}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return; // Don't go back
            }

            // Clear search before going back
            SearchText = string.Empty;
            if (CsvTableView != null)
                CsvTableView.RowFilter = string.Empty;

            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}