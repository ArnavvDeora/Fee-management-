using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ── Typed card — XAML binds to these properties, never to raw column names ──
    public class ScholarshipStudentCard
    {
        public string SerialNumber { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string Category { get; set; }
        public decimal QuarterlyFees { get; set; }
        public decimal PrevPending { get; set; }
        public decimal ScholarshipPct { get; set; }

        public string QuarterlyDisplay => $"₹{QuarterlyFees:N0}";
        public string PrevDisplay => $"₹{PrevPending:N0}";
        public string ScholarshipDisplay => ScholarshipPct > 0 ? $"{ScholarshipPct:N0}%" : "None";
        public string ScholarshipColor => ScholarshipPct > 0 ? "#E65100" : "#9E9E9E";

        public string CategoryBackground => Category?.ToUpper() switch
        {
            "SC" => "#E3F2FD",
            "ST" => "#E8EAF6",
            "OBC" => "#FFF8E1",
            "GEN" or "GENERAL" => "#E8F5E9",
            "BC" => "#F3E5F5",
            "GEN FW" => "#E0F2F1",
            "FW BC" => "#FCE4EC",
            "OBC FW" => "#FFF3E0",
            _ => "#F5F5F5"
        };

        public string CategoryForeground => Category?.ToUpper() switch
        {
            "SC" => "#1565C0",
            "ST" => "#283593",
            "OBC" => "#F57F17",
            "GEN" or "GENERAL" => "#2E7D32",
            "BC" => "#6A1B9A",
            "GEN FW" => "#00695C",
            "FW BC" => "#880E4F",
            "OBC FW" => "#E65100",
            _ => "#424242"
        };

        public DataRowView SourceRow { get; set; }
    }

    public partial class ScholarshipViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly AcademicCycleService _cycleService;
        private DataTable _fullSheetData;
        private string _currentSheetName;

        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        public ObservableCollection<string> ScholarshipFilterOptions { get; } = new()
        {
            "All Students",
            "With Scholarship Only"
        };

        [ObservableProperty]
        private ObservableCollection<ScholarshipStudentCard> studentCards = new();

        [ObservableProperty] private string selectedSheet;
        [ObservableProperty] private string sheetSearchText;
        [ObservableProperty] private DataView studentView;
        [ObservableProperty] private DataRowView selectedRow;
        [ObservableProperty] private string selectedScholarshipFilter = "All Students";
        [ObservableProperty] private string searchText;

        [ObservableProperty] private string studentName;
        [ObservableProperty] private string phoneNumber;
        [ObservableProperty] private decimal previousPending;
        [ObservableProperty] private decimal quarterlyFees;
        [ObservableProperty] private decimal scholarshipPercentage;
        [ObservableProperty] private decimal scholarshipDiscount;
        [ObservableProperty] private decimal adjustedQuarterly;
        [ObservableProperty] private decimal totalFees;

        [ObservableProperty] private int totalStudentsWithScholarship;
        [ObservableProperty] private decimal totalScholarshipAmount;
        [ObservableProperty] private string currentQuarterLabel = "";

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public ScholarshipViewModel(CsvDataService csvService,
                                    AcademicCycleService cycleService)
        {
            _csvService = csvService;
            _cycleService = cycleService;

            _cycleService.RunCycleCheck();
            CurrentQuarterLabel = $"Current quarter: {AcademicCycleService.CurrentQuarter()}";

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PROPERTY CHANGE HANDLERS
        // ════════════════════════════════════════════════════════════════════

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();
            IEnumerable<string> src = string.IsNullOrWhiteSpace(value)
                ? SheetNames
                : SheetNames.Where(n => n.ToLower().Contains(value.ToLower()));
            foreach (var n in src) FilteredSheetNames.Add(n);
        }

        partial void OnSelectedSheetChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) LoadSheetData(value);
        }

        partial void OnSelectedScholarshipFilterChanged(string value) => ApplyFilter();

        partial void OnScholarshipPercentageChanged(decimal value) => CalculateFees();

        partial void OnSelectedRowChanged(DataRowView value)
        {
            if (value != null) UpdateStudentDetails();
            else ClearStudentDetails();
        }

        // ════════════════════════════════════════════════════════════════════
        // DATA LOADING
        // ════════════════════════════════════════════════════════════════════

        private void LoadSheetData(string displayName)
        {
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);
            ApplyFilter();
            CalculateStatistics();
        }

        private void ApplyFilter()
        {
            if (_fullSheetData == null) return;

            StudentView = SelectedScholarshipFilter == "With Scholarship Only"
                ? _csvService.GetScholarshipView(_currentSheetName)
                : _fullSheetData.DefaultView;

            RebuildCards();
            CalculateStatistics();
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD LIST — keyword-based column search, no hardcoded column names
        // ════════════════════════════════════════════════════════════════════

        private void RebuildCards()
        {
            StudentCards.Clear();
            if (_fullSheetData == null) return;

            var table = _fullSheetData;

            DataColumn ColFind(params string[] keywords) =>
                table.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => keywords.All(k =>
                        c.ColumnName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0));

            decimal SafeDec(DataRow r, DataColumn c) =>
                c == null ? 0m : ParseDecimal(r[c]?.ToString()?.Trim());

            var nameCol = ColFind("name");
            var fatherCol = ColFind("father");
            var categoryCol = ColFind("category");
            var quarterlyCol = ColFind("quarterly") ?? ColFind("installment");
            var prevPendingCol = ColFind("previous", "pending") ?? ColFind("pending");
            var scholarshipCol = ColFind("scholarship");

            var sourceRows = (StudentView ?? _fullSheetData.DefaultView)
                .Cast<DataRowView>().ToList();

            int serial = 1;
            foreach (var drv in sourceRows)
            {
                DataRow row = drv.Row;
                string nm = nameCol != null ? row[nameCol]?.ToString()?.Trim() ?? "" : "";
                if (string.IsNullOrEmpty(nm)) continue;
                if (nm.Equals("Name", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.StartsWith("Note", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;

                string cat = (categoryCol != null
                    ? row[categoryCol]?.ToString()?.Trim() ?? "" : "").ToUpper();

                StudentCards.Add(new ScholarshipStudentCard
                {
                    SerialNumber = serial.ToString(),
                    Name = nm,
                    FatherName = fatherCol != null
                                       ? row[fatherCol]?.ToString()?.Trim() ?? "–" : "–",
                    Category = cat,
                    QuarterlyFees = SafeDec(row, quarterlyCol),
                    PrevPending = SafeDec(row, prevPendingCol),
                    ScholarshipPct = SafeDec(row, scholarshipCol),
                    SourceRow = drv
                });
                serial++;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // STUDENT DETAILS PANEL
        // ════════════════════════════════════════════════════════════════════

        private void UpdateStudentDetails()
        {
            if (SelectedRow == null) return;

            var table = SelectedRow.Row.Table;

            DataColumn ColFind(params string[] keywords) =>
                table.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => keywords.All(k =>
                        c.ColumnName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0));

            var nameCol = table.Columns.Cast<DataColumn>()
                                    .FirstOrDefault(c =>
                                        c.ColumnName.ToLower().Contains("name") &&
                                        !c.ColumnName.ToLower().Contains("father"));
            var phoneCol = ColFind("phone") ?? ColFind("mobile");
            var previousCol = ColFind("previous", "pending") ?? ColFind("pending");
            var quarterlyCol = ColFind("quarterly") ?? ColFind("installment");
            var scholarshipCol = ColFind("scholarship");

            if (nameCol != null) StudentName = SelectedRow[nameCol.ColumnName]?.ToString()?.Trim() ?? "";
            if (phoneCol != null) PhoneNumber = SelectedRow[phoneCol.ColumnName]?.ToString()?.Trim() ?? "";
            if (previousCol != null) PreviousPending = ParseDecimal(SelectedRow[previousCol.ColumnName]?.ToString());
            if (quarterlyCol != null) QuarterlyFees = ParseDecimal(SelectedRow[quarterlyCol.ColumnName]?.ToString());

            ScholarshipPercentage = scholarshipCol != null
                ? ParseDecimal(SelectedRow[scholarshipCol.ColumnName]?.ToString()) : 0m;

            CalculateFees();
        }

        private void CalculateFees()
        {
            ScholarshipDiscount = QuarterlyFees * (ScholarshipPercentage / 100);
            AdjustedQuarterly = QuarterlyFees - ScholarshipDiscount;
            TotalFees = PreviousPending + AdjustedQuarterly;
        }

        private void ClearStudentDetails()
        {
            StudentName = PhoneNumber = "";
            PreviousPending = QuarterlyFees = ScholarshipPercentage =
                ScholarshipDiscount = AdjustedQuarterly = TotalFees = 0m;
        }

        // ════════════════════════════════════════════════════════════════════
        // STATISTICS
        // ════════════════════════════════════════════════════════════════════

        private void CalculateStatistics()
        {
            if (_fullSheetData == null) return;

            DataColumn ColFind(params string[] keywords) =>
                _fullSheetData.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => keywords.All(k =>
                        c.ColumnName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0));

            var scholarshipCol = ColFind("scholarship");
            var quarterlyCol = ColFind("quarterly") ?? ColFind("installment");

            if (scholarshipCol == null || quarterlyCol == null)
            {
                TotalStudentsWithScholarship = 0;
                TotalScholarshipAmount = 0;
                return;
            }

            int count = 0; decimal totalDiscount = 0m;
            foreach (DataRow row in _fullSheetData.Rows)
            {
                decimal pct = ParseDecimal(row[scholarshipCol]?.ToString());
                if (pct > 0)
                {
                    count++;
                    totalDiscount += ParseDecimal(row[quarterlyCol]?.ToString()) * (pct / 100);
                }
            }

            TotalStudentsWithScholarship = count;
            TotalScholarshipAmount = totalDiscount;
        }

        // ════════════════════════════════════════════════════════════════════
        // COMMANDS
        // ════════════════════════════════════════════════════════════════════

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
                MessageBox.Show("Scholarship percentage must be between 0 and 100.",
                    "Invalid Percentage", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _csvService.ApplyScholarship(_currentSheetName, SelectedRow.Row, ScholarshipPercentage);

                MessageBox.Show(
                    $"✅ Scholarship Applied!\n\n" +
                    $"Student        : {StudentName}\n" +
                    $"Quarterly Fees : ₹{QuarterlyFees:F2}\n" +
                    $"Scholarship    : {ScholarshipPercentage}%\n" +
                    $"Discount       : ₹{ScholarshipDiscount:F2}\n" +
                    $"Adjusted Fee   : ₹{AdjustedQuarterly:F2}\n" +
                    $"Total Due      : ₹{TotalFees:F2}",
                    "Scholarship Applied", MessageBoxButton.OK, MessageBoxImage.Information);

                LoadSheetData(SelectedSheet);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to apply scholarship:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("Phone number column not found.", "Column Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (DataRow row in table.Rows)
            {
                bool match = true;
                int len = System.Math.Min(row.ItemArray.Length, SelectedRow.Row.ItemArray.Length);
                for (int i = 0; i < len; i++)
                    if (!row[i].Equals(SelectedRow.Row[i])) { match = false; break; }
                if (match) { row[phoneCol] = PhoneNumber; break; }
            }

            MessageBox.Show($"Phone number updated to: {PhoneNumber}",
                "Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public void SearchStudent()
        {
            if (_fullSheetData == null) return;
            if (string.IsNullOrWhiteSpace(SearchText)) { ApplyFilter(); return; }

            try
            {
                var table = StudentView?.Table ?? _fullSheetData;
                var filtered = table.Clone();
                foreach (DataRow row in table.Rows)
                    foreach (var item in row.ItemArray)
                        if (item != null && item.ToString()
                                .Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                        { filtered.ImportRow(row); break; }

                StudentView = filtered.DefaultView;
                RebuildCards();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Search failed: {ex.Message}",
                    "Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            ApplyFilter();
        }

        [RelayCommand]
        public void SelectStudentRow(object parameter)
        {
            if (parameter is ScholarshipStudentCard card && card.SourceRow != null)
                SelectedRow = card.SourceRow;
            else if (parameter is DataRowView drv)
                SelectedRow = drv;
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static decimal ParseDecimal(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;
            raw = raw.Replace("₹", "").Replace(",", "").Trim();
            return decimal.TryParse(raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal v) ? v : 0m;
        }
    }
}