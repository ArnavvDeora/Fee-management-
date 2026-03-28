using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ══════════════════════════════════════════════════════════════
    // TREE NODE MODELS
    // ══════════════════════════════════════════════════════════════

    /// <summary>Leaf node: represents one Year inside a Department.</summary>
    public partial class YearNode : ObservableObject
    {
        public string YearLabel { get; set; }   // e.g. "Year 1", "Passout"
        public int YearNumber { get; set; }   // 1-4, or 99 for passout
        public string Department { get; set; }   // Parent department code
        public string SheetName { get; set; }   // Internal table name in CsvDataService
        public int StudentCount { get; set; }

        [ObservableProperty] private bool isSelected;
    }

    /// <summary>Root node: represents one Department.</summary>
    public class DepartmentNode
    {
        public string DepartmentName { get; set; }   // e.g. "Mechanical Engineering"
        public string DepartmentCode { get; set; }   // e.g. "ME"
        public string StudentCountLabel => $"{YearNodes.Sum(y => y.StudentCount)} students";
        public ObservableCollection<YearNode> YearNodes { get; } = new();
    }

    // ══════════════════════════════════════════════════════════════
    // MAIN VIEW MODEL
    // ══════════════════════════════════════════════════════════════

    public partial class StudentListViewModel : ObservableObject
    {
        // ── Services ──────────────────────────────────────────────
        private readonly CsvDataService _csvService;
        private readonly AcademicCycleService _cycleService;
        private readonly PdfReportService _pdfService;
        private readonly FineCalculationService _fineService;

        // ── State ─────────────────────────────────────────────────
        private DataTable _fullData;           // currently displayed (unfiltered)
        private string _currentSheetName;   // sheet key in CsvDataService

        // ── Tree ──────────────────────────────────────────────────
        public ObservableCollection<DepartmentNode> DepartmentTree { get; } = new();

        // ── Grid ──────────────────────────────────────────────────
        [ObservableProperty] private DataView studentGridView;
        [ObservableProperty] private object selectedStudent;   // DataRowView

        // ── Filter / Sort ─────────────────────────────────────────
        [ObservableProperty] private string studentSearchText = "";
        [ObservableProperty] private string selectedStatusFilter = "All Students";
        [ObservableProperty] private string selectedSortOption = "Sr No.";

        public ObservableCollection<string> StatusFilterOptions { get; } = new()
        { "All Students", "Pending Fees Only", "No Pending Fees" };

        public ObservableCollection<string> SortOptions { get; } = new()
        { "Sr No.", "Name", "Highest Dues", "Category" };

        // ── Summary counters ──────────────────────────────────────
        [ObservableProperty] private int totalStudentsCount;
        [ObservableProperty] private int pendingCount;
        [ObservableProperty] private int paidCount;

        // ── Breadcrumb / labels ───────────────────────────────────
        [ObservableProperty] private string selectedDepartmentName = "";
        [ObservableProperty] private string selectedYearLabel = "";
        [ObservableProperty] private string activeSheetLabel = "";
        [ObservableProperty] private string sectionTitle = "Select a class from the sidebar";
        [ObservableProperty] private string sectionSubTitle = "";
        [ObservableProperty] private string currentQuarterLabel = "";
        [ObservableProperty] private string statusBarText = "No data loaded.";
        [ObservableProperty] private string lastRefreshedText = "";

        // ── Visibility helpers (bound to Visibility) ──────────────
        [ObservableProperty] private Visibility emptyStateVisible = Visibility.Visible;
        [ObservableProperty] private Visibility gridVisible = Visibility.Collapsed;
        [ObservableProperty] private Visibility breadcrumbVisible = Visibility.Collapsed;
        [ObservableProperty] private Visibility breadcrumbYearVisible = Visibility.Collapsed;

        // ── Constructor ───────────────────────────────────────────
        public StudentListViewModel(
            CsvDataService csvService,
            AcademicCycleService cycleService,
            PdfReportService pdfService,
            FineCalculationService fineService)
        {
            _csvService = csvService;
            _cycleService = cycleService;
            _pdfService = pdfService;
            _fineService = fineService;

            CurrentQuarterLabel = $"Current: {AcademicCycleService.CurrentQuarter()}";
            LastRefreshedText = $"Refreshed: {DateTime.Now:HH:mm}";

            RebuildTree();
        }

        // ══════════════════════════════════════════════════════════
        // TREE BUILDING
        // Reads all sheets from CsvDataService and groups them by
        // Department → Year.  Year is read from ExtendedProperties
        // (set by CsvDataService.LoadFile).
        // ══════════════════════════════════════════════════════════

        private void RebuildTree()
        {
            DepartmentTree.Clear();

            var allSheets = _csvService.GetAllSheets().ToList();

            // Group by department code
            var byDept = allSheets
                .GroupBy(t => t.ExtendedProperties["Department"]?.ToString() ?? "General")
                .OrderBy(g => g.Key);

            foreach (var deptGroup in byDept)
            {
                string deptCode = deptGroup.Key;
                string deptFull = ExpandDepartmentCode(deptCode);

                var deptNode = new DepartmentNode
                {
                    DepartmentCode = deptCode,
                    DepartmentName = deptFull
                };

                // Sub-group by year
                var byYear = deptGroup
                    .GroupBy(t =>
                    {
                        if (int.TryParse(t.ExtendedProperties["Year"]?.ToString(), out int y))
                            return y;
                        return 0;
                    })
                    .OrderBy(g => g.Key);

                foreach (var yearGroup in byYear)
                {
                    int yr = yearGroup.Key;

                    // Use the most-recently-uploaded sheet for this dept+year
                    var sheet = yearGroup.Last();
                    int studentCount = sheet.Rows.Cast<DataRow>()
                        .Count(r => !string.IsNullOrWhiteSpace(r.ItemArray
                            .Skip(1).FirstOrDefault()?.ToString()));

                    var yearNode = new YearNode
                    {
                        YearNumber = yr,
                        YearLabel = yr == 99 ? "Passout" :
                                       yr == 0 ? "Unknown" :
                                       $"Year {yr}",
                        Department = deptCode,
                        SheetName = sheet.TableName,
                        StudentCount = studentCount
                    };

                    deptNode.YearNodes.Add(yearNode);
                }

                if (deptNode.YearNodes.Count > 0)
                    DepartmentTree.Add(deptNode);
            }

            // Update status
            int total = DepartmentTree.Sum(d => d.YearNodes.Sum(y => y.StudentCount));
            StatusBarText = DepartmentTree.Count == 0
                ? "No data loaded. Upload an Excel file to get started."
                : $"{DepartmentTree.Count} department(s) loaded · {total} students total";
        }

        // ══════════════════════════════════════════════════════════
        // TREE SELECTION — called from code-behind
        // ══════════════════════════════════════════════════════════

        public void OnTreeSelectionChanged(object selectedItem)
        {
            // Deselect all year nodes
            foreach (var d in DepartmentTree)
                foreach (var y in d.YearNodes)
                    y.IsSelected = false;

            if (selectedItem is YearNode yn)
            {
                yn.IsSelected = true;
                LoadSheetForYearNode(yn);
            }
            else if (selectedItem is DepartmentNode dn)
            {
                LoadAllSheetsForDepartment(dn);
            }
        }

        private void LoadSheetForYearNode(YearNode yn)
        {
            var sheet = _csvService.GetSheet(yn.SheetName);
            if (sheet == null) return;

            _fullData = sheet;
            _currentSheetName = yn.SheetName;

            // Inject fines
            var meta = _csvService.GetSheetMetadata(yn.SheetName);
            var qStart = DetermineQuarterStart(sheet, meta?.Period);
            _fineService.InjectFinesIntoTable(sheet, qStart);

            // Update breadcrumb
            SelectedDepartmentName = ExpandDepartmentCode(yn.Department);
            SelectedYearLabel = yn.YearLabel;
            BreadcrumbVisible = Visibility.Visible;
            BreadcrumbYearVisible = Visibility.Visible;
            ActiveSheetLabel = meta?.Period ?? yn.SheetName;
            SectionTitle = $"{SelectedDepartmentName} — {yn.YearLabel}";
            SectionSubTitle = $"Quarter: {meta?.Quarter ?? "–"}   |   Period: {meta?.Period ?? "–"}";

            ApplyFilterAndSort();
            EmptyStateVisible = Visibility.Collapsed;
            GridVisible = Visibility.Visible;
        }

        private void LoadAllSheetsForDepartment(DepartmentNode dn)
        {
            // Merge all year sheets for this department into one view
            var merged = new DataTable();
            merged.Columns.Add("Year", typeof(string));
            merged.Columns.Add("Sr No", typeof(string));
            merged.Columns.Add("Name", typeof(string));
            merged.Columns.Add("Father Name", typeof(string));
            merged.Columns.Add("Category", typeof(string));
            merged.Columns.Add("Quarterly Fees", typeof(string));
            merged.Columns.Add("Previous Pending", typeof(string));
            merged.Columns.Add("Fine", typeof(string));
            merged.Columns.Add("Phone", typeof(string));

            foreach (var yn in dn.YearNodes)
            {
                var sheet = _csvService.GetSheet(yn.SheetName);
                if (sheet == null) continue;

                var meta = _csvService.GetSheetMetadata(yn.SheetName);
                var qStart = DetermineQuarterStart(sheet, meta?.Period);
                _fineService.InjectFinesIntoTable(sheet, qStart);

                foreach (DataRow row in sheet.Rows)
                {
                    string name = ColVal(sheet, row, c => c.Contains("name") && !c.Contains("father"));
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    merged.Rows.Add(
                        yn.YearLabel,
                        ColVal(sheet, row, c => c.Contains("sr")),
                        name,
                        ColVal(sheet, row, c => c.Contains("father")),
                        ColVal(sheet, row, c => c.Contains("category")),
                        ColVal(sheet, row, c => c.Contains("quarterly") || c.Contains("installment")),
                        ColVal(sheet, row, c => c.Contains("previous") && c.Contains("pending")),
                        ColVal(sheet, row, c => c.Equals("fine")),
                        ColVal(sheet, row, c => c.Contains("phone") || c.Contains("contact") || c.Contains("mobile"))
                    );
                }
            }

            _fullData = merged;
            _currentSheetName = null;

            SelectedDepartmentName = dn.DepartmentName;
            SelectedYearLabel = "All Years";
            BreadcrumbVisible = Visibility.Visible;
            BreadcrumbYearVisible = Visibility.Visible;
            ActiveSheetLabel = AcademicCycleService.CurrentQuarter();
            SectionTitle = $"{dn.DepartmentName} — All Years";
            SectionSubTitle = $"{dn.YearNodes.Count} year group(s)";

            ApplyFilterAndSort();
            EmptyStateVisible = Visibility.Collapsed;
            GridVisible = Visibility.Visible;
        }

        // ══════════════════════════════════════════════════════════
        // FILTER + SORT
        // ══════════════════════════════════════════════════════════

        partial void OnStudentSearchTextChanged(string value) => ApplyFilterAndSort();
        partial void OnSelectedStatusFilterChanged(string value) => ApplyFilterAndSort();
        partial void OnSelectedSortOptionChanged(string value) => ApplyFilterAndSort();

        private void ApplyFilterAndSort()
        {
            if (_fullData == null) return;

            // ── Find columns ──
            var nameCol = FindCol(_fullData, "name");
            var fatherCol = FindCol(_fullData, "father");
            var pendingCol = FindCol(_fullData, "previous", "pending") ??
                              FindCol(_fullData, "pending");
            var quarterlyCol = FindCol(_fullData, "quarterly", "installment");
            var fineCol = _fullData.Columns.Contains("Fine")
                                ? _fullData.Columns["Fine"] : null;

            // ── Build filtered DataTable ──
            var filtered = _fullData.Clone();

            foreach (DataRow row in _fullData.Rows)
            {
                // Name filter
                if (!string.IsNullOrWhiteSpace(StudentSearchText))
                {
                    string nm = row[nameCol ?? _fullData.Columns[1]]?.ToString() ?? "";
                    string fa = fatherCol != null ? row[fatherCol]?.ToString() ?? "" : "";
                    if (!nm.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase) &&
                        !fa.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Status filter
                if (SelectedStatusFilter != "All Students" && pendingCol != null)
                {
                    decimal pend = ReadDec(row, pendingCol) +
                                   ReadDec(row, quarterlyCol) +
                                   ReadDec(row, fineCol);
                    bool hasPending = pend > 0;

                    if (SelectedStatusFilter == "Pending Fees Only" && !hasPending) continue;
                    if (SelectedStatusFilter == "No Pending Fees" && hasPending) continue;
                }

                filtered.ImportRow(row);
            }

            // ── Sort ──
            DataView dv;
            if (SelectedSortOption == "Name" && nameCol != null)
                dv = new DataView(filtered) { Sort = nameCol.ColumnName };
            else if (SelectedSortOption == "Highest Dues" && pendingCol != null)
                dv = new DataView(filtered) { Sort = $"{pendingCol.ColumnName} DESC" };
            else if (SelectedSortOption == "Category" && FindCol(filtered, "category") is { } catCol)
                dv = new DataView(filtered) { Sort = catCol.ColumnName };
            else
                dv = new DataView(filtered);   // default: original order = Sr No.

            StudentGridView = dv;

            // ── Update summary counters ──
            TotalStudentsCount = filtered.Rows.Count;

            if (pendingCol != null)
            {
                int pending = 0, paid = 0;
                foreach (DataRow row in filtered.Rows)
                {
                    decimal pend = ReadDec(row, pendingCol) +
                                   ReadDec(row, quarterlyCol) +
                                   ReadDec(row, fineCol);
                    if (pend > 0) pending++; else paid++;
                }
                PendingCount = pending;
                PaidCount = paid;
            }
            else
            {
                PendingCount = 0;
                PaidCount = TotalStudentsCount;
            }

            LastRefreshedText = $"Refreshed: {DateTime.Now:HH:mm}";
        }

        // ══════════════════════════════════════════════════════════
        // UPLOAD COMMAND
        // ══════════════════════════════════════════════════════════

        [RelayCommand]
        public void UploadFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Upload Student Fee Excel File",
                Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _csvService.LoadFile(dlg.FileName);

                // Get what was detected
                var allSheets = _csvService.GetAllSheets().ToList();
                var newest = allSheets.LastOrDefault();

                string dept = newest?.ExtendedProperties["Department"]?.ToString() ?? "Unknown";
                string yr = newest?.ExtendedProperties["Year"]?.ToString() ?? "0";
                string qtr = newest?.ExtendedProperties["Quarter"]?.ToString() ?? "";
                string fullDept = ExpandDepartmentCode(dept);

                // Inform the user what was detected
                MessageBox.Show(
                    $"✅ File uploaded successfully!\n\n" +
                    $"🏫 Department Detected : {fullDept}\n" +
                    $"📅 Academic Year       : {FormatYear(yr)}\n" +
                    $"📋 Quarter             : {qtr}\n\n" +
                    $"Students are now listed under:\n" +
                    $"  {fullDept}  →  {FormatYear(yr)}",
                    "Upload Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                RebuildTree();
                StatusBarText = $"Loaded: {System.IO.Path.GetFileName(dlg.FileName)}  " +
                                $"→ {fullDept} / {FormatYear(yr)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Upload failed:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════
        // PROMOTION COMMAND
        // ══════════════════════════════════════════════════════════

        [RelayCommand]
        public void RunPromotion()
        {
            try
            {
                var transitions = _cycleService.RunCycleCheck();

                if (transitions.Count == 0)
                {
                    MessageBox.Show(
                        "✅ All students are in the correct academic year.\n" +
                        "No promotions were needed.",
                        "Promotion Check",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    string summary = string.Join("\n", transitions.Select(t =>
                        $"• {t.OldSheet}\n" +
                        $"  → {t.NewQuarter}  ({t.StudentsCarried} students carried forward)"));

                    MessageBox.Show(
                        $"🎓 Academic Cycle Update!\n\n{summary}\n\n" +
                        "Unpaid balances have been carried forward to the next quarter.\n" +
                        "Fee columns have been reset for the new period.",
                        "Promotion Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                RebuildTree();

                // If we're currently viewing a sheet, reload it
                if (!string.IsNullOrEmpty(_currentSheetName))
                {
                    // Find the matching year node and reload
                    foreach (var d in DepartmentTree)
                        foreach (var y in d.YearNodes)
                            if (y.SheetName == _currentSheetName)
                            {
                                LoadSheetForYearNode(y);
                                return;
                            }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Promotion check failed:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════
        // COLLECT FEE FOR ROW (called from row-detail button)
        // ══════════════════════════════════════════════════════════

        [RelayCommand]
        public void CollectFeeForRow(object row)
        {
            // Navigate to FeeCollectionView, pre-selecting this sheet
            var feeView = App.Current.Services.GetRequiredService<FeeCollectionView>();

            // Pre-select the sheet so the user lands on the right class
            if (feeView.DataContext is FeeCollectionViewModel feeVm &&
                !string.IsNullOrEmpty(_currentSheetName))
            {
                string displayName = _csvService.GetSheetDisplayNames()
                    .FirstOrDefault(d => _csvService.GetSheetNameFromDisplay(d) == _currentSheetName);
                if (!string.IsNullOrEmpty(displayName))
                    feeVm.SelectedSheet = displayName;
            }

            Application.Current.MainWindow.Content = feeView;
        }

        // ══════════════════════════════════════════════════════════
        // EXPORT COMMANDS
        // ══════════════════════════════════════════════════════════

        [RelayCommand]
        public void ExportPdf()
        {
            if (_fullData == null)
            {
                MessageBox.Show("No data to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"StudentList_{SectionTitle.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _pdfService.GenerateSummaryReport(
                    StudentGridView?.ToTable() ?? _fullData,
                    dlg.FileName,
                    SectionTitle,
                    null);

                if (MessageBox.Show("PDF exported! Open it?", "Success",
                        MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        { FileName = dlg.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ExportExcel()
        {
            if (_fullData == null)
            {
                MessageBox.Show("No data to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"StudentList_{SectionTitle.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.AddWorksheet("Students");
                var table = StudentGridView?.ToTable() ?? _fullData;

                // Header row
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = table.Columns[c].ColumnName;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor =
                        ClosedXML.Excel.XLColor.FromHtml("#1565C0");
                    cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }

                // Data rows
                for (int r = 0; r < table.Rows.Count; r++)
                    for (int c = 0; c < table.Columns.Count; c++)
                        ws.Cell(r + 2, c + 1).SetValue(table.Rows[r][c]?.ToString());

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                if (MessageBox.Show("Excel exported! Open it?", "Success",
                        MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        { FileName = dlg.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════
        // NAVIGATION
        // ══════════════════════════════════════════════════════════

        [RelayCommand]
        public void GoBack() =>
            Application.Current.MainWindow.Content =
                App.Current.Services.GetRequiredService<DashboardView>();

        // ══════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════

        private static DateTime DetermineQuarterStart(DataTable table, string periodString)
        {
            if (table.ExtendedProperties.ContainsKey("QuarterStart") &&
                table.ExtendedProperties["QuarterStart"] is DateTime stored)
                return stored;

            if (!string.IsNullOrWhiteSpace(periodString))
            {
                var parsed = FineCalculationService.TryParseQuarterStart(periodString);
                if (parsed.HasValue)
                {
                    table.ExtendedProperties["QuarterStart"] = parsed.Value;
                    return parsed.Value;
                }
            }
            return new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        }

        private static DataColumn FindCol(DataTable t, params string[] keywords) =>
            t?.Columns.Cast<DataColumn>()
              .FirstOrDefault(c => keywords.All(k =>
                  c.ColumnName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

        private static decimal ReadDec(DataRow row, DataColumn col)
        {
            if (col == null) return 0m;
            return decimal.TryParse(row[col]?.ToString()?.Trim(), out decimal v) ? v : 0m;
        }

        private static string ColVal(DataTable t, DataRow row, Func<string, bool> pred)
        {
            var col = t.Columns.Cast<DataColumn>()
                       .FirstOrDefault(c => pred(c.ColumnName.ToLower()));
            return col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
        }

        // ── Department code → full name ────────────────────────────
        private static readonly System.Collections.Generic.Dictionary<string, string>
            DeptNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "CS",          "Computer Science" },
            { "ME",          "Mechanical Engineering" },
            { "EE",          "Electrical Engineering" },
            { "CE",          "Civil Engineering" },
            { "ECE",         "Electronics & Communication" },
            { "IT",          "Information Technology" },
            { "CHE",         "Chemical Engineering" },
            { "BT",          "Biotechnology" },
            { "MECHATRONICS","Mechatronics" },
            { "General",     "General" },
        };

        private static string ExpandDepartmentCode(string code) =>
            DeptNames.TryGetValue(code, out string full) ? full : code;

        private static string FormatYear(string yr) =>
            yr switch
            {
                "1" => "Year 1 (1st Year)",
                "2" => "Year 2 (2nd Year)",
                "3" => "Year 3 (3rd Year)",
                "4" => "Year 4 (4th Year)",
                "99" => "Passout",
                _ => $"Year {yr}"
            };
    }
}