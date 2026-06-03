using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ══════════════════════════════════════════════════════════════════════════
    // Card model for cross-quarter student search results
    // ══════════════════════════════════════════════════════════════════════════
    public class StudentQuarterResultCard
    {
        // Identity
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string FatherNameDisplay => string.IsNullOrEmpty(FatherName) ? "" : $"S/o {FatherName}";
        public string Category { get; set; }

        // Sheet context
        public string QuarterLabel { get; set; }   // e.g. "Feb-Apr"
        public string DeptLabel { get; set; }   // e.g. "Mechatronics"
        public string SemLabel { get; set; }   // e.g. "Sem 3"
        public string SheetName { get; set; }   // internal sheet key

        // Fee amounts
        public decimal QuarterlyFee { get; set; }
        public decimal PrevPending { get; set; }
        public decimal TotalDue { get; set; }

        public string QuarterlyDisplay => $"₹{QuarterlyFee:N0}";
        public string PrevPendingDisplay => $"₹{PrevPending:N0}";
        public string TotalDueDisplay => $"₹{TotalDue:N0}";

        // Colours driven by balance
        public string StripeColor => PrevPending > 0 ? "#E53935" : TotalDue > 0 ? "#FB8C00" : "#43A047";
        public string CardBorderColor => PrevPending > 0 ? "#FFCDD2" : TotalDue > 0 ? "#FFE0B2" : "#C8E6C9";
        public string StatusText => TotalDue <= 0 ? "✔ Paid" : PrevPending > 0 ? "⚠ Overdue" : "⏳ Pending";
        public string StatusBg => TotalDue <= 0 ? "#E8F5E9" : PrevPending > 0 ? "#FFEBEE" : "#FFF3E0";
        public string StatusBorder => TotalDue <= 0 ? "#A5D6A7" : PrevPending > 0 ? "#EF9A9A" : "#FFB74D";
        public string StatusFg => TotalDue <= 0 ? "#2E7D32" : PrevPending > 0 ? "#C62828" : "#E65100";
        public string TotalDueBg => TotalDue <= 0 ? "#E8F5E9" : "#FFEBEE";
        public string TotalDueFg => TotalDue <= 0 ? "#2E7D32" : "#C62828";

        // Category pill colours
        public string CategoryBg => Category?.ToUpper() switch
        {
            "SC" => "#E3F2FD",
            "ST" => "#E8EAF6",
            "OBC" => "#FFF8E1",
            "GEN" or "GENERAL" => "#E8F5E9",
            "BC" => "#F3E5F5",
            _ => "#F5F5F5"
        };
        public string CategoryFg => Category?.ToUpper() switch
        {
            "SC" => "#1565C0",
            "ST" => "#283593",
            "OBC" => "#F57F17",
            "GEN" or "GENERAL" => "#2E7D32",
            "BC" => "#6A1B9A",
            _ => "#424242"
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ReportsViewModel
    // ══════════════════════════════════════════════════════════════════════════
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PdfReportService _pdfService;
        private readonly PaymentLogService _paymentLogService;

        private DataTable _originalData;
        private string _currentSheetName;

        // ── Sheet list ────────────────────────────────────────────────────────
        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        // ── Report types ──────────────────────────────────────────────────────
        public ObservableCollection<string> ReportTypes { get; } = new()
        {
            "Individual Student Report",
            "Pending Fees Summary",
            "All Students Summary",
            "Custom Filter Report",
            "Payment Transaction Logs"
        };

        // ── Quarter / dept / status filters ──────────────────────────────────
        public ObservableCollection<string> QuarterFilterOptions { get; } = new()
        { "All Quarters", "Feb-Apr", "May-Jul", "Aug-Oct", "Nov-Jan" };

        public ObservableCollection<string> DepartmentFilterOptions { get; } = new();
        public ObservableCollection<string> StatusFilterOptions { get; } = new()
        { "All Status", "Pending Only", "Paid Only" };

        // ── Cross-quarter results ─────────────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<StudentQuarterResultCard> crossQuarterResults = new();

        // ── Observable properties ─────────────────────────────────────────────
        [ObservableProperty] private string selectedReportType = "Individual Student Report";
        [ObservableProperty] private string selectedSheet;
        [ObservableProperty] private string sheetSearchText;
        [ObservableProperty] private DataView currentSheetView;
        [ObservableProperty] private string searchText;
        [ObservableProperty] private DataRowView selectedRow;
        [ObservableProperty] private string selectedQuarterFilter = "All Quarters";
        [ObservableProperty] private string selectedDepartmentFilter = "All Departments";
        [ObservableProperty] private string selectedStatusFilter = "All Status";

        // ── Stat chips ────────────────────────────────────────────────────────
        [ObservableProperty] private int totalStudents;
        [ObservableProperty] private int studentsWithPending;
        [ObservableProperty] private string totalPendingAmount = "₹0";
        [ObservableProperty] private string activeQuarterLabel = "";
        [ObservableProperty] private int totalTransactions;
        [ObservableProperty] private string totalPaymentsCollected = "₹0";

        // ── Sheet metadata labels ─────────────────────────────────────────────
        [ObservableProperty] private string sheetQuarterLabel = "";
        [ObservableProperty] private string sheetDeptLabel = "";

        // ── Result / display labels ───────────────────────────────────────────
        [ObservableProperty] private string resultCountLabel = "";
        [ObservableProperty] private string crossQuarterResultCount = "";

        // ── Visibility flags ─────────────────────────────────────────────────
        [ObservableProperty] private System.Windows.Visibility singleSheetVisible = System.Windows.Visibility.Collapsed;
        [ObservableProperty] private System.Windows.Visibility crossQuarterVisible = System.Windows.Visibility.Collapsed;
        [ObservableProperty] private System.Windows.Visibility emptyStateVisible = System.Windows.Visibility.Visible;
        [ObservableProperty] private System.Windows.Visibility paymentLogsVisible = System.Windows.Visibility.Collapsed;

        // ── Payment log date range ────────────────────────────────────────────
        [ObservableProperty] private DateTime paymentLogStartDate = DateTime.Now.AddMonths(-1);
        [ObservableProperty] private DateTime paymentLogEndDate = DateTime.Now;

        // ═════════════════════════════════════════════════════════════════
        // COURSE CONTEXT BAR + "Switch class" popup picker
        //
        // Same pattern as FeeCollectionViewModel / ScholarshipViewModel.
        // SelectedSheet is still the source of truth — these new properties
        // just give it a friendlier face. Setting SelectedSheet from
        // PickCourseCommand goes through OnSelectedSheetChanged, so the
        // existing load pipeline keeps working unchanged.
        //
        // CourseChoice is reused from FeeCollectionViewModel.cs (same
        // namespace) — no duplication.
        // ═════════════════════════════════════════════════════════════════

        public ObservableCollection<CourseChoice> AvailableCourses { get; } = new();

        [ObservableProperty] private string currentCourseTitle = "No class selected";
        [ObservableProperty] private string currentCourseSubtitle = "Click 'Switch class' to begin";
        [ObservableProperty] private string currentCourseInitials = "?";
        [ObservableProperty] private string currentCourseAvatarBg = "#ECEFF1";
        [ObservableProperty] private string currentCourseAvatarFg = "#546E7A";
        [ObservableProperty] private bool isClassPickerOpen;
        [ObservableProperty] private string classPickerSearchText;
        [ObservableProperty] private ObservableCollection<CourseChoice> filteredCourses = new();

        // ══════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════════
        public ReportsViewModel(CsvDataService csvService,
                                PdfReportService pdfService,
                                PaymentLogService paymentLogService)
        {
            _csvService = csvService;
            _pdfService = pdfService;
            _paymentLogService = paymentLogService;

            ActiveQuarterLabel = AcademicCycleService.CurrentQuarter();

            // Populate sheet names
            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }

            // Build department filter from all loaded sheets
            BuildDepartmentFilter();

            // Build the course-card list for the popup picker
            BuildAvailableCourses();
        }

        private void BuildDepartmentFilter()
        {
            DepartmentFilterOptions.Clear();
            DepartmentFilterOptions.Add("All Departments");

            var depts = _csvService.GetAllSheets()
                .Select(t => t.ExtendedProperties["Department"]?.ToString() ?? "")
                .Where(d => !string.IsNullOrEmpty(d) && d != "MISC")
                .Distinct()
                .OrderBy(d => d);

            foreach (var d in depts) DepartmentFilterOptions.Add(d);

            SelectedDepartmentFilter = "All Departments";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  COURSE PICKER PLUMBING
        //  ----------------------------------------------------------------------
        //  Parses every loaded sheet's ExtendedProperties into a CourseChoice
        //  so the popup picker can show clean labels ("Mechanical Engineering —
        //  Sem 2") instead of raw "FileName - TableName" strings.
        // ════════════════════════════════════════════════════════════════════════

        private void BuildAvailableCourses()
        {
            AvailableCourses.Clear();
            FilteredCourses.Clear();

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                if (displayName.Contains("_PaymentHistory") ||
                    displayName.ToLower().Contains("payment history"))
                    continue;

                string tableName = _csvService.GetSheetNameFromDisplay(displayName);
                var sheet = _csvService.GetSheet(tableName);
                if (sheet == null) continue;

                var choice = BuildCourseChoice(sheet, displayName);
                if (choice == null) continue;

                AvailableCourses.Add(choice);
                FilteredCourses.Add(choice);
            }

            // Sort dept, then semester so courses group naturally.
            var sorted = AvailableCourses
                .OrderBy(c => c.DepartmentSortOrder)
                .ThenBy(c => c.Semester)
                .ThenBy(c => c.Title)
                .ToList();
            AvailableCourses.Clear();
            FilteredCourses.Clear();
            foreach (var c in sorted) { AvailableCourses.Add(c); FilteredCourses.Add(c); }
        }

        private CourseChoice BuildCourseChoice(DataTable sheet, string displayName)
        {
            string deptCode = ExtractDeptCode(sheet);
            if (string.IsNullOrEmpty(deptCode) || deptCode == "PASSOUT") return null;

            int semester = 0;
            if (sheet.ExtendedProperties.ContainsKey("Semester") &&
                int.TryParse(sheet.ExtendedProperties["Semester"]?.ToString(), out int s))
                semester = s;

            string quarter = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
            string custom = sheet.ExtendedProperties["DisplayName"]?.ToString();
            string deptName = DeptFullName(deptCode);

            string title = !string.IsNullOrWhiteSpace(custom)
                ? custom
                : (semester > 0 ? $"{deptName} — Sem {semester}" : deptName);

            int calYear = DateTime.Now.Year;
            if (quarter == "Nov-Jan" && DateTime.Now.Month <= 1) calYear--;

            string subtitle = string.IsNullOrEmpty(quarter) ? deptName : $"{quarter} {calYear}";
            int studentCount = CountStudentRows(sheet);
            if (studentCount > 0) subtitle += $"  ·  {studentCount} students";

            return new CourseChoice
            {
                DisplayName = displayName,
                Title = title,
                Subtitle = subtitle,
                DepartmentCode = deptCode,
                DepartmentName = deptName,
                Semester = semester,
                Initials = DeptInitials(deptCode),
                AccentBg = DeptAccentBg(deptCode),
                AccentFg = DeptAccentFg(deptCode),
                DepartmentSortOrder = DeptSortOrder(deptCode),
                StudentCount = studentCount,
            };
        }

        // ── Dept helpers (kept inline so the VM is self-contained) ──────────────

        private static string ExtractDeptCode(DataTable sheet)
        {
            string meta = sheet.ExtendedProperties["Department"]?.ToString();
            if (!string.IsNullOrEmpty(meta) && meta != "General" && meta != "MISC") return meta;
            string n = (sheet.TableName ?? "").ToUpper();
            if (n.Contains("PASSOUT") || n.Contains("PASS OUT") || n.Contains("PASS-OUT")) return "PASSOUT";
            if (n.Contains("MECHATRONICS")) return "MECHATRONICS";
            if (n.Contains("ME") || n.Contains("MECH") || n.Contains("T&D") || n.Contains("TOOL")) return "ME";
            if (n.Contains("EE") || n.Contains("ELECTRICAL")) return "EE";
            if (n.Contains("CSE") || n.Contains("CS") || n.Contains("COMPUTER")) return "CSE";
            return meta;
        }

        private static string DeptFullName(string code) => code switch
        {
            "ME" => "Mechanical Engineering",
            "MECHATRONICS" => "Mechatronics Engineering",
            "EE" => "Electrical Engineering",
            "CSE" => "Computer Science Engineering",
            "PASSOUT" => "Passed Out",
            "MISC" => "Miscellaneous",
            _ => code ?? "Unknown"
        };

        private static string DeptInitials(string code) => code switch
        {
            "ME" => "ME",
            "MECHATRONICS" => "MT",
            "EE" => "EE",
            "CSE" => "CS",
            _ => "?"
        };

        private static string DeptAccentBg(string code) => code switch
        {
            "ME" => "#E3F2FD",
            "MECHATRONICS" => "#E8EAF6",
            "EE" => "#FFF3E0",
            "CSE" => "#E8F5E9",
            _ => "#ECEFF1"
        };

        private static string DeptAccentFg(string code) => code switch
        {
            "ME" => "#1565C0",
            "MECHATRONICS" => "#283593",
            "EE" => "#E65100",
            "CSE" => "#2E7D32",
            _ => "#546E7A"
        };

        private static int DeptSortOrder(string code) => code switch
        {
            "ME" => 1,
            "MECHATRONICS" => 2,
            "EE" => 3,
            "CSE" => 4,
            _ => 99
        };

        private static int CountStudentRows(DataTable t)
        {
            var nameCol = t.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                c.ColumnName.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0);
            if (nameCol == null) return t.Rows.Count;
            return t.Rows.Cast<DataRow>().Count(r =>
            {
                string s = r[nameCol]?.ToString()?.Trim() ?? "";
                return !string.IsNullOrEmpty(s) && s.Length <= 60
                    && !s.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("Note", StringComparison.OrdinalIgnoreCase);
            });
        }

        // ── Picker commands bound by the popup ──────────────────────────────────

        [RelayCommand]
        public void OpenClassPicker()
        {
            // Rebuild every time the popup opens. Catches the case where files
            // were loaded after the VM was constructed (or none yet at all).
            BuildAvailableCourses();
            ClassPickerSearchText = string.Empty;
            ApplyPickerSearch();
            IsClassPickerOpen = true;
        }

        [RelayCommand]
        public void CloseClassPicker() => IsClassPickerOpen = false;

        [RelayCommand]
        public void PickCourse(CourseChoice choice)
        {
            if (choice == null) return;
            IsClassPickerOpen = false;
            // Assigning SelectedSheet triggers OnSelectedSheetChanged → LoadSheetData
            // → ApplyCurrentView through the existing pipeline.
            SelectedSheet = choice.DisplayName;
        }

        partial void OnClassPickerSearchTextChanged(string value) => ApplyPickerSearch();

        private void ApplyPickerSearch()
        {
            FilteredCourses.Clear();
            var src = string.IsNullOrWhiteSpace(ClassPickerSearchText)
                ? AvailableCourses
                : (IEnumerable<CourseChoice>)AvailableCourses.Where(c =>
                    (c.Title ?? "").IndexOf(ClassPickerSearchText, StringComparison.OrdinalIgnoreCase) >= 0
                 || (c.Subtitle ?? "").IndexOf(ClassPickerSearchText, StringComparison.OrdinalIgnoreCase) >= 0
                 || (c.DepartmentName ?? "").IndexOf(ClassPickerSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var c in src) FilteredCourses.Add(c);
        }

        // ── Refresh the big context-bar label whenever SelectedSheet changes ───

        private void RefreshCurrentCourseLabel()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            {
                CurrentCourseTitle = "No class selected";
                CurrentCourseSubtitle = "Click 'Switch class' to begin";
                CurrentCourseInitials = "?";
                CurrentCourseAvatarBg = "#ECEFF1";
                CurrentCourseAvatarFg = "#546E7A";
                return;
            }
            var match = AvailableCourses.FirstOrDefault(c =>
                string.Equals(c.DisplayName, SelectedSheet, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                CurrentCourseTitle = SelectedSheet;
                CurrentCourseSubtitle = "";
                CurrentCourseInitials = "?";
                CurrentCourseAvatarBg = "#ECEFF1";
                CurrentCourseAvatarFg = "#546E7A";
                return;
            }
            CurrentCourseTitle = match.Title;
            CurrentCourseSubtitle = match.Subtitle;
            CurrentCourseInitials = match.Initials;
            CurrentCourseAvatarBg = match.AccentBg;
            CurrentCourseAvatarFg = match.AccentFg;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PROPERTY CHANGE HANDLERS
        // ══════════════════════════════════════════════════════════════════════

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();
            var src = string.IsNullOrWhiteSpace(value)
                ? SheetNames
                : (IEnumerable<string>)SheetNames.Where(n => n.ToLower().Contains(value.ToLower()));
            foreach (var n in src) FilteredSheetNames.Add(n);
        }

        partial void OnSelectedSheetChanged(string value)
        {
            // Refresh the context-bar label first — runs for both real values
            // and the empty/null case (shows the "No class selected" hint).
            RefreshCurrentCourseLabel();

            if (string.IsNullOrEmpty(value)) return;

            // Clear any cross-quarter search
            SearchText = string.Empty;
            CrossQuarterResults.Clear();

            _currentSheetName = _csvService.GetSheetNameFromDisplay(value);
            var table = _csvService.GetSheet(_currentSheetName);

            if (table != null)
            {
                _originalData = table;
                CurrentSheetView = ApplySheetFilters(_originalData.DefaultView);
                UpdateStatistics();
                UpdateSheetLabels(table);
            }

            ShowSingleSheet();
        }

        partial void OnSelectedReportTypeChanged(string value)
        {
            PaymentLogsVisible = value == "Payment Transaction Logs"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (value == "Payment Transaction Logs")
                LoadPaymentLogs();
        }

        partial void OnSelectedQuarterFilterChanged(string value) => ApplyCurrentView();
        partial void OnSelectedDepartmentFilterChanged(string value) => ApplyCurrentView();
        partial void OnSelectedStatusFilterChanged(string value) => ApplyCurrentView();

        private void ApplyCurrentView()
        {
            if (CrossQuarterResults.Count > 0)
                ApplyCrossQuarterFilters();
            else if (_originalData != null)
            {
                CurrentSheetView = ApplySheetFilters(_originalData.DefaultView);
                UpdateStatistics();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // DATA LOADING
        // ══════════════════════════════════════════════════════════════════════

        private void LoadPaymentLogs()
        {
            var logs = _paymentLogService.GetLogsByDateRange(PaymentLogStartDate, PaymentLogEndDate);
            var logsTable = _paymentLogService.GetLogsAsDataTable(logs);

            _originalData = logsTable;
            CurrentSheetView = logsTable.DefaultView;
            TotalTransactions = logs.Count;
            TotalPaymentsCollected = $"₹{logs.Sum(l => l.AmountPaid):N2}";

            ShowSingleSheet();
        }

        // ══════════════════════════════════════════════════════════════════════
        // SHEET FILTER (quarter + status on the single-sheet DataGrid)
        // ══════════════════════════════════════════════════════════════════════

        private DataView ApplySheetFilters(DataView dv)
        {
            if (dv == null) return dv;

            var table = dv.Table;
            var nameCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase));
            var qCol = table.Columns.Cast<DataColumn>()
                .Where(c => !c.ColumnName.StartsWith("_"))
                .FirstOrDefault(c => c.ColumnName.IndexOf("quarterly", StringComparison.OrdinalIgnoreCase) >= 0
                                  || c.ColumnName.IndexOf("installment", StringComparison.OrdinalIgnoreCase) >= 0);
            var pCol = table.Columns.Cast<DataColumn>()
                .Where(c => !c.ColumnName.StartsWith("_"))
                .FirstOrDefault(c => c.ColumnName.IndexOf("previous", StringComparison.OrdinalIgnoreCase) >= 0
                                  && c.ColumnName.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0);

            // Build filtered DataTable
            var filtered = table.Clone();
            foreach (DataRow row in table.Rows)
            {
                // Skip non-student rows
                if (nameCol != null)
                {
                    string nm = row[nameCol]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(nm)) continue;
                    if (nm.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;
                }

                // Status filter
                if (SelectedStatusFilter != "All Status" && (qCol != null || pCol != null))
                {
                    decimal q = ParseDec(row, qCol);
                    decimal p = ParseDec(row, pCol);
                    decimal due = q + p;

                    if (SelectedStatusFilter == "Pending Only" && due <= 0) continue;
                    if (SelectedStatusFilter == "Paid Only" && due > 0) continue;
                }

                filtered.ImportRow(row);
            }

            var result = filtered.DefaultView;
            ResultCountLabel = $"{filtered.Rows.Count} students";
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // STATISTICS
        // ══════════════════════════════════════════════════════════════════════

        private void UpdateStatistics()
        {
            if (_originalData == null) return;

            var nameCol = _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase));

            // Only count real student rows
            var studentRows = _originalData.Rows.Cast<DataRow>()
                .Where(r =>
                {
                    if (nameCol == null) return true;
                    string nm = r[nameCol]?.ToString()?.Trim() ?? "";
                    return !string.IsNullOrEmpty(nm)
                        && !nm.Equals("Name", StringComparison.OrdinalIgnoreCase)
                        && !nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)
                        && nm.Length <= 60
                        && !nm.Contains(":-")
                        && !nm.Contains("Per Day");
                }).ToList();

            TotalStudents = studentRows.Count;

            // Find fee columns
            var qCol = _originalData.Columns.Cast<DataColumn>()
                .Where(c => !c.ColumnName.StartsWith("_"))
                .FirstOrDefault(c => c.ColumnName.IndexOf("quarterly", StringComparison.OrdinalIgnoreCase) >= 0
                                  || c.ColumnName.IndexOf("installment", StringComparison.OrdinalIgnoreCase) >= 0);
            var pCol = _originalData.Columns.Cast<DataColumn>()
                .Where(c => !c.ColumnName.StartsWith("_"))
                .FirstOrDefault(c => c.ColumnName.IndexOf("previous", StringComparison.OrdinalIgnoreCase) >= 0
                                  && c.ColumnName.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0);

            decimal totalPending = 0m;
            int countPending = 0;

            foreach (var row in studentRows)
            {
                decimal q = ParseDec(row, qCol);
                decimal p = ParseDec(row, pCol);
                decimal due = q + p;
                if (due > 0) { totalPending += due; countPending++; }
            }

            StudentsWithPending = countPending;
            TotalPendingAmount = $"₹{totalPending:N2}";
        }

        private void UpdateSheetLabels(DataTable table)
        {
            SheetQuarterLabel = table.ExtendedProperties["Quarter"]?.ToString() ?? "";
            string deptCode = table.ExtendedProperties["Department"]?.ToString() ?? "";
            string semStr = table.ExtendedProperties["Semester"]?.ToString() ?? "";

            // Build a readable dept label
            SheetDeptLabel = deptCode switch
            {
                "ME" => "Mechanical Engg.",
                "MECHATRONICS" => "Mechatronics",
                "EE" => "Electrical Engg.",
                "CSE" => "Computer Science",
                "PASSOUT" => "Pass-outs",
                _ => deptCode
            };

            if (int.TryParse(semStr, out int sem) && sem > 0)
                SheetDeptLabel += $" · Sem {sem}";
        }

        // ══════════════════════════════════════════════════════════════════════
        // CROSS-QUARTER STUDENT SEARCH
        // Searches ALL loaded sheets for the student name, then shows one card
        // per (sheet × student) hit with full balance information.
        // ══════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void SearchStudent()
        {
            if (_originalData == null && string.IsNullOrWhiteSpace(SearchText))
                return;

            // If no search text, fall back to sheet filter
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                CrossQuarterResults.Clear();
                if (_originalData != null)
                {
                    CurrentSheetView = ApplySheetFilters(_originalData.DefaultView);
                    UpdateStatistics();
                    ShowSingleSheet();
                }
                return;
            }

            // Cross-quarter search across ALL sheets
            CrossQuarterResults.Clear();
            string query = SearchText.Trim();

            foreach (var sheet in _csvService.GetAllSheets())
            {
                string quarter = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
                string dept = sheet.ExtendedProperties["Department"]?.ToString() ?? "";
                string semStr = sheet.ExtendedProperties["Semester"]?.ToString() ?? "";

                // Quarter filter
                if (SelectedQuarterFilter != "All Quarters" && quarter != SelectedQuarterFilter) continue;
                // Dept filter
                if (SelectedDepartmentFilter != "All Departments" && dept != SelectedDepartmentFilter) continue;

                var nameCol = sheet.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase));
                var fatherCol = sheet.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.IndexOf("father", StringComparison.OrdinalIgnoreCase) >= 0);
                var catCol = sheet.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0);
                var qCol = sheet.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => c.ColumnName.IndexOf("quarterly", StringComparison.OrdinalIgnoreCase) >= 0
                                      || c.ColumnName.IndexOf("installment", StringComparison.OrdinalIgnoreCase) >= 0);
                var pCol = sheet.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => c.ColumnName.IndexOf("previous", StringComparison.OrdinalIgnoreCase) >= 0
                                      && c.ColumnName.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0);

                if (nameCol == null) continue;

                foreach (DataRow row in sheet.Rows)
                {
                    string nm = row[nameCol]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(nm)) continue;
                    if (nm.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;

                    // Name match (also check father name)
                    string father = fatherCol != null ? row[fatherCol]?.ToString()?.Trim() ?? "" : "";
                    bool nameMatch = nm.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool fatherMatch = father.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch && !fatherMatch) continue;

                    decimal q = ParseDec(row, qCol);
                    decimal p = ParseDec(row, pCol);
                    decimal due = q + p;

                    // Status filter
                    if (SelectedStatusFilter == "Pending Only" && due <= 0) continue;
                    if (SelectedStatusFilter == "Paid Only" && due > 0) continue;

                    string cat = (catCol != null ? row[catCol]?.ToString()?.Trim() ?? "" : "").ToUpper();
                    string deptFull = dept switch
                    {
                        "ME" => "Mechanical",
                        "MECHATRONICS" => "Mechatronics",
                        "EE" => "Electrical",
                        "CSE" => "CSE",
                        "PASSOUT" => "Pass-out",
                        _ => dept
                    };

                    string semLabel = int.TryParse(semStr, out int sem) && sem > 0
                        ? $"Sem {sem}" : "";

                    CrossQuarterResults.Add(new StudentQuarterResultCard
                    {
                        Name = nm,
                        FatherName = father,
                        Category = cat,
                        QuarterLabel = quarter,
                        DeptLabel = deptFull,
                        SemLabel = semLabel,
                        SheetName = sheet.TableName,
                        QuarterlyFee = q,
                        PrevPending = p,
                        TotalDue = due
                    });
                }
            }

            CrossQuarterResultCount = $"{CrossQuarterResults.Count} result(s) across "
                + $"{CrossQuarterResults.Select(r => r.SheetName).Distinct().Count()} sheet(s)";

            ShowCrossQuarter();
        }

        private void ApplyCrossQuarterFilters()
        {
            // Re-run search to apply new filter values
            SearchStudent();
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            CrossQuarterResults.Clear();
            CrossQuarterResultCount = "";

            if (_originalData != null)
            {
                CurrentSheetView = ApplySheetFilters(_originalData.DefaultView);
                UpdateStatistics();
                ShowSingleSheet();
            }
            else
            {
                ShowEmpty();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // VISIBILITY HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private void ShowSingleSheet()
        {
            SingleSheetVisible = System.Windows.Visibility.Visible;
            CrossQuarterVisible = System.Windows.Visibility.Collapsed;
            EmptyStateVisible = System.Windows.Visibility.Collapsed;
        }

        private void ShowCrossQuarter()
        {
            SingleSheetVisible = System.Windows.Visibility.Collapsed;
            CrossQuarterVisible = System.Windows.Visibility.Visible;
            EmptyStateVisible = System.Windows.Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            SingleSheetVisible = System.Windows.Visibility.Collapsed;
            CrossQuarterVisible = System.Windows.Visibility.Collapsed;
            EmptyStateVisible = System.Windows.Visibility.Visible;
        }

        // ══════════════════════════════════════════════════════════════════════
        // GENERATE REPORT
        // ══════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void GenerateReport()
        {
            if (SelectedReportType == "Payment Transaction Logs")
            {
                GeneratePaymentLogsReport();
                return;
            }

            if (string.IsNullOrEmpty(SelectedSheet))
            {
                MessageBox.Show("Please select a sheet first.", "No Sheet Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var metadata = _csvService.GetSheetMetadata(_currentSheetName);

            switch (SelectedReportType)
            {
                case "Individual Student Report": GenerateIndividualReport(metadata); break;
                case "Pending Fees Summary": GeneratePendingFeesReport(metadata); break;
                case "All Students Summary": GenerateAllStudentsReport(metadata); break;
                case "Custom Filter Report": GenerateCustomFilterReport(metadata); break;
                default: GenerateIndividualReport(metadata); break;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT EXCEL
        // ══════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void ExportExcel()
        {
            DataTable dataToExport = null;
            string title = "Report";

            if (CrossQuarterResults.Count > 0)
            {
                // Build a DataTable from cross-quarter cards
                dataToExport = new DataTable();
                dataToExport.Columns.Add("Name");
                dataToExport.Columns.Add("Father Name");
                dataToExport.Columns.Add("Category");
                dataToExport.Columns.Add("Department");
                dataToExport.Columns.Add("Semester");
                dataToExport.Columns.Add("Quarter");
                dataToExport.Columns.Add("Quarterly Fee");
                dataToExport.Columns.Add("Carried Forward");
                dataToExport.Columns.Add("Total Due");
                dataToExport.Columns.Add("Status");

                foreach (var c in CrossQuarterResults)
                    dataToExport.Rows.Add(c.Name, c.FatherName, c.Category,
                        c.DeptLabel, c.SemLabel, c.QuarterLabel,
                        c.QuarterlyFee, c.PrevPending, c.TotalDue, c.StatusText);

                title = $"Student Search - {SearchText}";
            }
            else if (_originalData != null)
            {
                dataToExport = CurrentSheetView?.ToTable() ?? _originalData;
                title = SelectedSheet ?? "Report";
            }

            if (dataToExport == null || dataToExport.Rows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.AddWorksheet("Report");

                // Header row
                for (int c = 0; c < dataToExport.Columns.Count; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = dataToExport.Columns[c].ColumnName;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
                    cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }

                // Data rows
                for (int r = 0; r < dataToExport.Rows.Count; r++)
                {
                    for (int c = 0; c < dataToExport.Columns.Count; c++)
                    {
                        string val = dataToExport.Rows[r][c]?.ToString() ?? "";
                        var cell = ws.Cell(r + 2, c + 1);
                        if (decimal.TryParse(val, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out decimal num))
                            cell.Value = num;
                        else
                            cell.Value = val;
                    }
                    if (r % 2 == 1)
                        ws.Row(r + 2).Style.Fill.BackgroundColor =
                            ClosedXML.Excel.XLColor.FromHtml("#F5F5F5");
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                if (MessageBox.Show("Excel exported! Open it?", "Success",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        { FileName = dlg.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // INDIVIDUAL REPORT GENERATORS (all preserved from original)
        // ══════════════════════════════════════════════════════════════════════

        private void GenerateIndividualReport(CsvDataService.SheetMetadata metadata)
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Please select a student row first.", "No Student Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Student_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateStudentReport(SelectedRow.Row, dlg.FileName, metadata);
                    MessageBox.Show("Individual Student Report Generated!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenFile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GeneratePendingFeesReport(CsvDataService.SheetMetadata metadata)
        {
            var pendingView = _csvService.GetPendingFeesView(_currentSheetName);
            if (pendingView == null || pendingView.Count == 0)
            {
                MessageBox.Show("No pending fees found.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Pending_Fees_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(pendingView.Table, dlg.FileName,
                        "Pending Fees Report", metadata);
                    MessageBox.Show($"Report Generated — {pendingView.Count} students with pending fees.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenFile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateAllStudentsReport(CsvDataService.SheetMetadata metadata)
        {
            if (_originalData == null || _originalData.Rows.Count == 0)
            {
                MessageBox.Show("No data available.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"All_Students_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(_originalData, dlg.FileName,
                        "All Students Fee Report", metadata);
                    MessageBox.Show($"Report Generated — {_originalData.Rows.Count} students.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenFile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateCustomFilterReport(CsvDataService.SheetMetadata metadata)
        {
            if (CurrentSheetView == null || CurrentSheetView.Count == 0)
            {
                MessageBox.Show("No data. Apply a search filter first.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dataToExport = CurrentSheetView.ToTable();
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Custom_Report_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(dataToExport, dlg.FileName,
                        "Custom Filtered Report", metadata);
                    MessageBox.Show($"Report Generated — {dataToExport.Rows.Count} records.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenFile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GeneratePaymentLogsReport()
        {
            var logs = _paymentLogService.GetLogsByDateRange(PaymentLogStartDate, PaymentLogEndDate);

            if (logs.Count == 0)
            {
                MessageBox.Show($"No transactions between {PaymentLogStartDate:dd-MM-yyyy} and {PaymentLogEndDate:dd-MM-yyyy}.",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|CSV Files (*.csv)|*.csv",
                FileName = $"Payment_Logs_{PaymentLogStartDate:yyyyMMdd}_to_{PaymentLogEndDate:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    if (dlg.FileName.EndsWith(".csv"))
                    {
                        _paymentLogService.ExportToCsv(dlg.FileName, logs);
                        MessageBox.Show($"CSV exported — {logs.Count} transactions, " +
                            $"₹{logs.Sum(l => l.AmountPaid):N2} total.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var logsTable = _paymentLogService.GetLogsAsDataTable(logs);
                        _pdfService.GenerateSummaryReport(logsTable, dlg.FileName,
                            $"Payment Logs ({PaymentLogStartDate:dd-MM-yyyy} to {PaymentLogEndDate:dd-MM-yyyy})", null);
                        MessageBox.Show($"PDF exported — {logs.Count} transactions.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    OpenFile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // NAVIGATION
        // ══════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void GoBack()
        {
            SearchText = string.Empty;
            if (CurrentSheetView != null) CurrentSheetView.RowFilter = string.Empty;
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // ══════════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private static decimal ParseDec(DataRow row, DataColumn col)
        {
            if (col == null) return 0m;
            string raw = row[col]?.ToString()?.Replace("₹", "").Replace(",", "").Trim() ?? "";
            return decimal.TryParse(raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal v) ? v : 0m;
        }

        private static void OpenFile(string path)
        {
            if (MessageBox.Show("Would you like to open the file?", "Open",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}