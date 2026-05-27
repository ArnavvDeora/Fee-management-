using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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

        // ═════════════════════════════════════════════════════════════════
        // COURSE CONTEXT BAR + "Switch class" popup picker
        //
        // Same pattern as FeeCollectionViewModel — the screen no longer shows
        // the raw "FileName - SheetName" dropdown. Instead the current sheet
        // is rendered as a clean "Mechanical Engineering — Sem 2" header and
        // the admin uses a popup grid of mini course cards to switch.
        //
        // CourseChoice is shared from FeeCollectionViewModel (same namespace).
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

        // ═════════════════════════════════════════════════════════════════
        // SCHOLARSHIP REASON  (free-text, e.g. "Merit", "Sports", "SC quota")
        //
        // Stored in the Scholarship_Reason column on the sheet via
        // CsvDataService.ApplyScholarship. Reset each quarter alongside
        // the percentage when AcademicCycleService.Advance() zeros all
        // non-ID columns.
        // ═════════════════════════════════════════════════════════════════

        [ObservableProperty] private string scholarshipReason = "";

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public ScholarshipViewModel(CsvDataService csvService,
                                AcademicCycleService cycleService)
        {
            _csvService = csvService;
            _cycleService = cycleService;

            // NOTE: RunCycleCheck() is intentionally NOT called here.
            // It fires once in FeeCollectionViewModel which shows the user the
            // transition notification. Calling it again here would double-advance
            // sheets and show duplicate MessageBoxes.
            CurrentQuarterLabel = $"Current quarter: {AcademicCycleService.CurrentQuarter()}";

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }

            BuildAvailableCourses();
        }

        // ═════════════════════════════════════════════════════════════════
        // COURSE PICKER PLUMBING
        // ─────────────────────────────────────────────────────────────────
        // Parses every loaded sheet's ExtendedProperties into a CourseChoice
        // (Department / Semester / Quarter) so the popup picker can show
        // clean labels instead of "FileName - TableName" mush.
        // ═════════════════════════════════════════════════════════════════

        private void BuildAvailableCourses()
        {
            AvailableCourses.Clear();
            FilteredCourses.Clear();

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                // Skip payment-history support sheets — they're not real courses.
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

            // Sort: dept, then semester — groups Mech sems together, then MT, etc.
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

            if (_cycleService != null)
            {
                DateTime imp = _cycleService.GetOriginalImportDate(sheet.TableName);
                if (imp != DateTime.MinValue) subtitle += $"  ·  Uploaded {imp:dd MMM yyyy}";
            }

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

        // ── Dept helpers (mirror of FeeCollectionViewModel for self-containment) ─

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

        // ─── Picker commands bound by the popup ───────────────────────────

        [RelayCommand]
        public void OpenClassPicker()
        {
            // Rebuild on open — catches the case where files were loaded
            // after the VM was constructed (or none were loaded at all yet).
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
            // Setting SelectedSheet triggers OnSelectedSheetChanged → LoadSheetData.
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

        // ── Refresh the big course-context-bar label whenever the sheet changes ─

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
            RefreshCurrentCourseLabel();
        }

        partial void OnSelectedScholarshipFilterChanged(string value) => ApplyFilter();

        partial void OnScholarshipPercentageChanged(decimal value) => CalculateFees();

        // Live-as-you-type search. Filters the already-built StudentCards list
        // in-place against the visible card fields (Name / Father / Category /
        // SerialNumber). No DataTable.Clone()-style trickery, no risk of
        // "Column X does not belong" errors — just LINQ over the typed cards.
        partial void OnSearchTextChanged(string value) => ApplySearch();

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

            // RebuildCards populates _allCards from the freshly-loaded sheet
            // AND then calls ApplySearchAndFilter to populate the visible
            // StudentCards. Going through ApplyFilter directly would skip
            // _allCards population and leave the list empty.
            RebuildCards();
            CalculateStatistics();
        }

        private void ApplyFilter()
        {
            if (_fullSheetData == null) return;
            // The filter dropdown now applies entirely in memory via
            // ApplySearchAndFilter — no DataView swapping, no risk of breaking
            // bindings or triggering "Column X does not belong" failures.
            ApplySearchAndFilter();
            CalculateStatistics();
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD LIST — keyword-based column search, no hardcoded column names
        //
        // _allCards holds the unfiltered master list. StudentCards is the
        // VISIBLE subset (filtered by SearchText). Search runs against
        // _allCards so the user can clear the search and get everything back
        // without re-querying the underlying DataTable.
        // ════════════════════════════════════════════════════════════════════

        private readonly List<ScholarshipStudentCard> _allCards = new();

        private void RebuildCards()
        {
            _allCards.Clear();
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

            // IMPORTANT: source from the FULL sheet so search isn't constrained
            // by the active filter dropdown. The filter applies AFTER, in ApplySearch.
            var sourceRows = _fullSheetData.DefaultView.Cast<DataRowView>().ToList();

            int serial = 1;
            foreach (var drv in sourceRows)
            {
                // Each row is wrapped in its own try/catch so one bad row
                // (e.g. a stray header in the middle) can't kill the whole
                // rebuild and leave the user with an empty list.
                try
                {
                    DataRow row = drv.Row;
                    string nm = nameCol != null ? row[nameCol]?.ToString()?.Trim() ?? "" : "";
                    if (string.IsNullOrEmpty(nm)) continue;
                    if (nm.Equals("Name", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.StartsWith("Note", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;

                    string cat = (categoryCol != null
                        ? row[categoryCol]?.ToString()?.Trim() ?? "" : "").ToUpper();

                    _allCards.Add(new ScholarshipStudentCard
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
                catch
                {
                    // Skip malformed rows silently — common when CSVs have
                    // half-filled rows or merged header cells.
                    continue;
                }
            }

            // Push the master list into the visible collection, then apply
            // any active search and filter.
            ApplySearchAndFilter();
        }

        // ────────────────────────────────────────────────────────────────────
        // Combined search + dropdown-filter applied entirely in memory against
        // _allCards. Never touches the DataTable, so no risk of "Column X does
        // not belong to table" errors.
        // ────────────────────────────────────────────────────────────────────
        private void ApplySearchAndFilter()
        {
            IEnumerable<ScholarshipStudentCard> q = _allCards;

            // Dropdown filter
            if (SelectedScholarshipFilter == "With Scholarship Only")
                q = q.Where(c => c.ScholarshipPct > 0);

            // Free-text search — matches name, father, category, or serial.
            // Whitespace-trimmed and case-insensitive, so "  kartik  " and
            // "KARTIK" both match a card whose Name is "Kartik".
            string needle = (SearchText ?? "").Trim();
            if (!string.IsNullOrEmpty(needle))
            {
                q = q.Where(c =>
                       (c.Name ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.FatherName ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Category ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.SerialNumber ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            StudentCards.Clear();
            foreach (var c in q) StudentCards.Add(c);
        }

        // Public entry point for the live-search hook AND the Search button.
        private void ApplySearch() => ApplySearchAndFilter();

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
            var reasonCol = ColFind("scholarship", "reason");

            if (nameCol != null) StudentName = SelectedRow[nameCol.ColumnName]?.ToString()?.Trim() ?? "";
            if (phoneCol != null) PhoneNumber = SelectedRow[phoneCol.ColumnName]?.ToString()?.Trim() ?? "";
            if (previousCol != null) PreviousPending = ParseDecimal(SelectedRow[previousCol.ColumnName]?.ToString());
            if (quarterlyCol != null) QuarterlyFees = ParseDecimal(SelectedRow[quarterlyCol.ColumnName]?.ToString());

            // ScholarshipPercentage must come from the "Scholarship" column ONLY,
            // not the reason column (ColFind("scholarship") returns the first
            // column containing "scholarship" — guard against picking up the
            // reason column by accident).
            DataColumn pctCol = (scholarshipCol != null &&
                                 scholarshipCol.ColumnName.IndexOf("reason",
                                     StringComparison.OrdinalIgnoreCase) < 0)
                                ? scholarshipCol
                                : table.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                                    c.ColumnName.IndexOf("scholarship", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    c.ColumnName.IndexOf("reason", StringComparison.OrdinalIgnoreCase) < 0);

            ScholarshipPercentage = pctCol != null
                ? ParseDecimal(SelectedRow[pctCol.ColumnName]?.ToString()) : 0m;

            // Load any existing reason from the row so admin sees what was set.
            ScholarshipReason = reasonCol != null
                ? SelectedRow[reasonCol.ColumnName]?.ToString()?.Trim() ?? ""
                : "";

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
            ScholarshipReason = "";
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
                _csvService.ApplyScholarship(_currentSheetName, SelectedRow.Row,
                                             ScholarshipPercentage, ScholarshipReason);

                // Tell other open ViewModels (FeeCollection / Class / etc.) to refresh
                // — so a scholarship applied here shows up immediately in the fee
                // collection cards, without needing an app restart.
                App.RaiseFeeDataChanged();

                MessageBox.Show(
                    $"✅ Scholarship Applied!\n\n" +
                    $"Student        : {StudentName}\n" +
                    $"Quarterly Fees : ₹{QuarterlyFees:F2}\n" +
                    $"Scholarship    : {ScholarshipPercentage}%" +
                    (string.IsNullOrWhiteSpace(ScholarshipReason)
                        ? "\n"
                        : $"  ({ScholarshipReason})\n") +
                    $"Discount       : ₹{ScholarshipDiscount:F2}\n" +
                    $"Adjusted Fee   : ₹{AdjustedQuarterly:F2}\n" +
                    $"Total Due      : ₹{TotalFees:F2}\n\n" +
                    $"ℹ️ This applies to the current quarter only.\n" +
                    $"It will not carry forward when the quarter advances.",
                    "Scholarship Applied", MessageBoxButton.OK, MessageBoxImage.Information);

                LoadSheetData(SelectedSheet);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to apply scholarship:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Bound to the preset percentage chips (0 / 10 / 25 / 50 / 100).
        /// CommandParameter is the string "10" etc. Setting ScholarshipPercentage
        /// triggers OnScholarshipPercentageChanged → CalculateFees so the
        /// discount/adjusted/total numbers update live.
        /// </summary>
        [RelayCommand]
        public void SetQuickPercentage(string pctText)
        {
            if (decimal.TryParse(pctText, out decimal v) && v >= 0 && v <= 100)
                ScholarshipPercentage = v;
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

        // The Search BUTTON is now a fallback for users who prefer to press
        // Enter. Live-as-you-type via OnSearchTextChanged already does the
        // filtering; this command just forces another pass and is safe to call
        // even when SearchText is empty.
        [RelayCommand]
        public void SearchStudent() => ApplySearchAndFilter();

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            // OnSearchTextChanged will fire ApplySearchAndFilter — but call it
            // explicitly too so the list refreshes even if SearchText was
            // already empty (no change → no partial method invocation).
            ApplySearchAndFilter();
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
        /// <summary>
        /// Persists all in-memory scholarship changes to the Excel files on disk.
        /// The old XAML binding was SaveChangesCommand — the new XAML uses SaveToFileCommand
        /// to avoid confusion with FeeCollectionViewModel.SaveChangesCommand.
        /// </summary>
        [RelayCommand]
        public void SaveToFile()
        {
            try
            {
                _csvService.SaveFile();
                MessageBox.Show(
                    "✅ All scholarship changes saved successfully!",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Save failed:\n\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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