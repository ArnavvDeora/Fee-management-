using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ScholarshipViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private DataTable _fullSheetData;
        private string _currentSheetName; // Store actual sheet name (not display name)

        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        // Filter options
        public ObservableCollection<string> ScholarshipFilterOptions { get; } = new()
        {
            "All Students",
            "With Scholarship Only"
        };

        [ObservableProperty]
        private string selectedSheet;

        [ObservableProperty]
        private string sheetSearchText;

        [ObservableProperty]
        private DataView studentView;

        [ObservableProperty]
        private DataRowView selectedRow;

        [ObservableProperty]
        private string selectedScholarshipFilter = "All Students";

        [ObservableProperty]
        private string searchText;

        // Student Details
        [ObservableProperty]
        private string studentName;

        [ObservableProperty]
        private string phoneNumber;

        [ObservableProperty]
        private decimal previousPending;

        [ObservableProperty]
        private decimal quarterlyFees;

        [ObservableProperty]
        private decimal scholarshipPercentage;

        [ObservableProperty]
        private decimal scholarshipDiscount;

        [ObservableProperty]
        private decimal adjustedQuarterly;

        [ObservableProperty]
        private decimal totalFees;

        [ObservableProperty]
        private int totalStudentsWithScholarship;

        [ObservableProperty]
        private decimal totalScholarshipAmount;

        public ScholarshipViewModel(CsvDataService csvService)
        {
            _csvService = csvService;

            // Use display names that include the time period
            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }
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
            if (!string.IsNullOrEmpty(value))
            {
                LoadSheetData(value);
            }
        }

        partial void OnSelectedScholarshipFilterChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnScholarshipPercentageChanged(decimal value)
        {
            CalculateFees();
        }

        private void LoadSheetData(string displayName)
        {
            // Convert display name to actual sheet name
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);

            ApplyFilter();
            CalculateStatistics();
        }

        private void ApplyFilter()
        {
            if (_fullSheetData == null) return;

            if (SelectedScholarshipFilter == "All Students")
            {
                StudentView = _fullSheetData.DefaultView;
            }
            else if (SelectedScholarshipFilter == "With Scholarship Only")
            {
                StudentView = _csvService.GetScholarshipView(_currentSheetName);
            }

            CalculateStatistics();
        }

        partial void OnSelectedRowChanged(DataRowView value)
        {
            if (value != null)
            {
                UpdateStudentDetails();
            }
            else
            {
                ClearStudentDetails();
            }
        }

        private void UpdateStudentDetails()
        {
            if (SelectedRow == null) return;

            var table = SelectedRow.Row.Table;

            // Get student name
            var nameCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("name") &&
                                   !c.ColumnName.ToLower().Contains("father"));

            if (nameCol != null)
                StudentName = SelectedRow[nameCol.ColumnName]?.ToString()?.Trim() ?? "Unknown";

            // Get phone number
            var phoneCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("phone") ||
                                   c.ColumnName.ToLower().Contains("mobile"));

            if (phoneCol != null)
                PhoneNumber = SelectedRow[phoneCol.ColumnName]?.ToString()?.Trim() ?? "";

            // Get previous/pending fees
            var previousCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending"));

            if (previousCol != null)
            {
                string raw = SelectedRow[previousCol.ColumnName]?.ToString()?.Trim();
                PreviousPending = ParseDecimal(raw);
            }

            // Get quarterly fees
            var quarterlyCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly") ||
                                   c.ColumnName.ToLower().Contains("current"));

            if (quarterlyCol != null)
            {
                string raw = SelectedRow[quarterlyCol.ColumnName]?.ToString()?.Trim();
                QuarterlyFees = ParseDecimal(raw);
            }

            // Get scholarship percentage
            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            if (scholarshipCol != null)
            {
                string raw = SelectedRow[scholarshipCol.ColumnName]?.ToString()?.Trim();
                ScholarshipPercentage = ParseDecimal(raw);
            }
            else
            {
                ScholarshipPercentage = 0;
            }

            CalculateFees();
        }

        private void CalculateFees()
        {
            // Calculate scholarship discount on quarterly fees
            ScholarshipDiscount = QuarterlyFees * (ScholarshipPercentage / 100);

            // Adjusted quarterly after scholarship
            AdjustedQuarterly = QuarterlyFees - ScholarshipDiscount;

            // Total = Previous Pending + Adjusted Quarterly
            TotalFees = PreviousPending + AdjustedQuarterly;
        }

        private decimal ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            value = value.Trim().Replace("₹", "").Replace(",", "");

            if (decimal.TryParse(value, out decimal result))
                return result;

            return 0;
        }

        private void ClearStudentDetails()
        {
            StudentName = string.Empty;
            PhoneNumber = string.Empty;
            PreviousPending = 0;
            QuarterlyFees = 0;
            ScholarshipPercentage = 0;
            ScholarshipDiscount = 0;
            AdjustedQuarterly = 0;
            TotalFees = 0;
        }

        private void CalculateStatistics()
        {
            if (_fullSheetData == null) return;

            var scholarshipCol = _fullSheetData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            var quarterlyCol = _fullSheetData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly") ||
                                   c.ColumnName.ToLower().Contains("current"));

            if (scholarshipCol == null || quarterlyCol == null)
            {
                TotalStudentsWithScholarship = 0;
                TotalScholarshipAmount = 0;
                return;
            }

            int count = 0;
            decimal totalDiscount = 0;

            foreach (DataRow row in _fullSheetData.Rows)
            {
                string scholarshipStr = row[scholarshipCol]?.ToString()?.Trim() ?? "";
                decimal scholarshipPercent = ParseDecimal(scholarshipStr);

                if (scholarshipPercent > 0)
                {
                    count++;

                    string quarterlyStr = row[quarterlyCol]?.ToString()?.Trim();
                    decimal quarterly = ParseDecimal(quarterlyStr);

                    totalDiscount += quarterly * (scholarshipPercent / 100);
                }
            }

            TotalStudentsWithScholarship = count;
            TotalScholarshipAmount = totalDiscount;
        }

        [RelayCommand]
        public void ApplyScholarship()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Please select a student first.", "No Student Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ScholarshipPercentage < 0 || ScholarshipPercentage > 100)
            {
                MessageBox.Show("Scholarship percentage must be between 0 and 100.", "Invalid Percentage",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Apply scholarship using the service
                _csvService.ApplyScholarship(_currentSheetName, SelectedRow.Row, ScholarshipPercentage);

                MessageBox.Show(
                    $"✅ Scholarship Applied!\n\n" +
                    $"Student: {StudentName}\n" +
                    $"Previous Pending: ₹{PreviousPending:F2}\n" +
                    $"Quarterly Fees: ₹{QuarterlyFees:F2}\n" +
                    $"Scholarship: {ScholarshipPercentage}%\n" +
                    $"Discount: ₹{ScholarshipDiscount:F2}\n" +
                    $"Adjusted Quarterly: ₹{AdjustedQuarterly:F2}\n" +
                    $"Total Fees: ₹{TotalFees:F2}\n\n" +
                    $"Formula: Previous ({PreviousPending:F2}) + Adjusted Quarterly ({AdjustedQuarterly:F2}) = Total ({TotalFees:F2})\n\n" +
                    $"Note: Save changes to persist the scholarship.",
                    "Scholarship Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh the view
                LoadSheetData(SelectedSheet);
                CalculateStatistics();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Failed to apply scholarship: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void UpdatePhoneNumber()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Please select a student first.", "No Student Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var table = _fullSheetData;
            var phoneCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("phone") ||
                                   c.ColumnName.ToLower().Contains("mobile"));

            if (phoneCol == null)
            {
                MessageBox.Show("Phone number column not found in the sheet.", "Column Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Find the row in the table
            DataRow targetRow = null;
            foreach (DataRow row in table.Rows)
            {
                bool match = true;
                for (int i = 0; i < Math.Min(row.ItemArray.Length, SelectedRow.Row.ItemArray.Length); i++)
                {
                    if (!row[i].Equals(SelectedRow.Row[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    targetRow = row;
                    break;
                }
            }

            if (targetRow != null)
            {
                targetRow[phoneCol] = PhoneNumber;
                MessageBox.Show(
                    $"Phone number updated to: {PhoneNumber}",
                    "Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void SearchStudent()
        {
            if (_fullSheetData == null) return;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                ApplyFilter();
                return;
            }

            try
            {
                var table = StudentView.Table;
                var filtered = table.Clone();

                foreach (DataRow row in table.Rows)
                {
                    foreach (var item in row.ItemArray)
                    {
                        if (item != null && item.ToString().Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.ImportRow(row);
                            break;
                        }
                    }
                }

                StudentView = filtered.DefaultView;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Search failed: {ex.Message}",
                    "Search Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            ApplyFilter();
        }

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

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}