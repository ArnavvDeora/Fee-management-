using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ClassViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PdfReportService _pdfReportService;
        private readonly AcademicCycleService _cycleService;
        private readonly QuarterHistoryService _historyService;
        private DataTable _originalData;
        private string _currentSheetName;

        // ==========================
        // DEPARTMENT STRUCTURE
        // ==========================

        public ObservableCollection<DepartmentInfo> Departments { get; } = new();
        public ObservableCollection<CourseInfo> AllCourses { get; } = new();
        public ObservableCollection<CourseInfo> FilteredCourses { get; } = new();

        [ObservableProperty]
        private DepartmentInfo selectedDepartment;

        // ==========================
        // VIEW MODES
        // ==========================

        [ObservableProperty]
        private bool isDepartmentViewMode = true;

        [ObservableProperty]
        private bool isDepartmentManagementMode = false;

        [ObservableProperty]
        private bool isDataViewMode = false;

        // ==========================
        // FILTER OPTIONS
        // ==========================

        public ObservableCollection<string> YearFilterOptions { get; } = new();
        public ObservableCollection<string> QuarterFilterOptions { get; } = new();
        public ObservableCollection<string> StatusFilterOptions { get; } = new();

        [ObservableProperty]
        private string selectedYearFilter = "All Years";

        [ObservableProperty]
        private string selectedQuarterFilter = "All Quarters";

        [ObservableProperty]
        private string selectedStatusFilter = "All Status";

        [ObservableProperty]
        private string globalSearchText;

        // ==========================
        // DASHBOARD STATISTICS
        // ==========================

        [ObservableProperty]
        private int totalCourses;

        [ObservableProperty]
        private int totalStudents;

        [ObservableProperty]
        private int totalPendingStudents;

        [ObservableProperty]
        private decimal totalPendingAmount;

        [ObservableProperty]
        private int activeQuarters;

        [ObservableProperty]
        private string departmentSummary;

        // ==========================
        // DATA VIEW PROPERTIES
        // ==========================
        [ObservableProperty]
        private ObservableCollection<StudentCardRow> studentCardRows = new();
        [ObservableProperty] private int dataViewTotalStudents;
        [ObservableProperty] private int dataViewPaidStudents;
        [ObservableProperty] private int dataViewPendingStudents;
        [ObservableProperty] private decimal dataViewPendingAmount;
        [ObservableProperty]
        private DataView csvTableView;

        [ObservableProperty]
        private string currentViewTitle;

        [ObservableProperty]
        private DataRowView selectedRow;

        public ObservableCollection<string> Columns { get; } = new();

        [ObservableProperty]
        private string selectedColumn;

        [ObservableProperty]
        private string newValue;

        [ObservableProperty]
        private string searchText;

        [ObservableProperty]
        private string rowCountDisplay = "0 students";

        // ==========================
        // HISTORY / TIMELINE
        // ==========================

        public QuarterTimelineViewModel Timeline { get; }

        [ObservableProperty]
        private CourseInfo currentCourse;

        [ObservableProperty]
        private string originalFileAddedBadge = "";

        [ObservableProperty]
        private string currentQuarterBadge = "";

        [ObservableProperty]
        private bool isShowingSnapshot = false;

        [ObservableProperty]
        private string snapshotBanner = "";

        // ==========================
        // CONSTRUCTOR
        // ==========================

        public ClassViewModel(CsvDataService csvService, PdfReportService pdfReportService,
            AcademicCycleService cycleService = null, QuarterHistoryService historyService = null)
        {
            _csvService = csvService;
            _pdfReportService = pdfReportService;
            _cycleService = cycleService;
            _historyService = historyService;

            if (_historyService != null && _cycleService != null)
            {
                Timeline = new QuarterTimelineViewModel(_historyService, _cycleService);

                Timeline.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(QuarterTimelineViewModel.IsShowingSnapshot))
                    {
                        IsShowingSnapshot = Timeline.IsShowingSnapshot;
                        SnapshotBanner = Timeline.SnapshotBanner;

                        if (Timeline.IsShowingSnapshot && Timeline.ActiveSnapshot != null)
                            SwapToSnapshot(Timeline.ActiveSnapshot);
                        else
                            RestoreLiveData();
                    }

                    if (e.PropertyName == nameof(QuarterTimelineViewModel.SnapshotBanner))
                        SnapshotBanner = Timeline.SnapshotBanner;
                };
            }

            cycleService?.RunCycleCheck();

            InitializeDepartments();
            InitializeFilters();
            App.FeeDataChanged += () =>
            {
                if (IsDataViewMode)
                    BuildStudentCards();
            };
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedYearFilter) ||
                    e.PropertyName == nameof(SelectedQuarterFilter) ||
                    e.PropertyName == nameof(SelectedStatusFilter) ||
                    e.PropertyName == nameof(GlobalSearchText))
                    ApplyFilters();

                if (e.PropertyName == nameof(SearchText))
                    ApplyDataViewSearch();
            };
        }

        // ==========================
        // INITIALIZATION
        // ==========================

        private void InitializeDepartments()
        {
            Departments.Clear();

            Departments.Add(new DepartmentInfo
            {
                Name = "Mechanical Engineering",
                Code = "ME",
                Years = 4,
                Color = "#FF5722",
                Icon = "⚙️"
            });

            Departments.Add(new DepartmentInfo
            {
                Name = "Mechatronics Engineering",
                Code = "MECHATRONICS",
                Years = 3,
                Color = "#3F51B5",
                Icon = "🤖"
            });

            Departments.Add(new DepartmentInfo
            {
                Name = "Electrical Engineering",
                Code = "EE",
                Years = 3,
                Color = "#FFC107",
                Icon = "⚡"
            });

            Departments.Add(new DepartmentInfo
            {
                Name = "Computer Science and Engineering",
                Code = "CSE",
                Years = 3,
                Color = "#4CAF50",
                Icon = "💻"
            });

            Departments.Add(new DepartmentInfo
            {
                Name = "Miscellaneous Courses",
                Code = "MISC",
                Years = 0,
                Color = "#9C27B0",
                Icon = "📚"
            });

            Departments.Add(new DepartmentInfo
            {
                Name = "Pass-outs / Graduates",
                Code = "PASSOUT",
                Years = 0,
                Color = "#607D8B",
                Icon = "🎓"
            });
        }

        private void InitializeFilters()
        {
            YearFilterOptions.Clear();
            YearFilterOptions.Add("All Years");
            YearFilterOptions.Add("Year 1");
            YearFilterOptions.Add("Year 2");
            YearFilterOptions.Add("Year 3");
            YearFilterOptions.Add("Year 4");

            QuarterFilterOptions.Clear();
            QuarterFilterOptions.Add("All Quarters");
            QuarterFilterOptions.Add("Aug-Oct");
            QuarterFilterOptions.Add("Nov-Jan");
            QuarterFilterOptions.Add("Feb-Apr");
            QuarterFilterOptions.Add("May-Jun");

            StatusFilterOptions.Clear();
            StatusFilterOptions.Add("All Status");
            StatusFilterOptions.Add("Has Pending");
            StatusFilterOptions.Add("Fully Paid");
            StatusFilterOptions.Add("No Data");
        }

        // ==========================
        // DEPARTMENT SELECTION
        // ==========================

        [RelayCommand]
        public void SelectDepartment(object parameter)
        {
            string deptCode = parameter?.ToString();
            if (string.IsNullOrEmpty(deptCode)) return;

            var dept = Departments.FirstOrDefault(d => d.Code == deptCode);
            if (dept == null) return;

            SelectedDepartment = dept;
            LoadDepartmentCourses();

            IsDepartmentViewMode = false;
            IsDepartmentManagementMode = true;
            IsDataViewMode = false;
        }

        private void LoadDepartmentCourses()
        {
            AllCourses.Clear();
            FilteredCourses.Clear();

            try
            {
                var allSheets = _csvService.GetAllSheets();

                foreach (var sheet in allSheets)
                {
                    var courseInfo = ParseCourseFromSheet(sheet);
                    if (courseInfo != null && courseInfo.DepartmentCode == SelectedDepartment.Code)
                    {
                        AllCourses.Add(courseInfo);
                        FilteredCourses.Add(courseInfo);
                    }
                }

                CalculateDashboardStatistics();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading courses: {ex.Message}",
                    "Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private CourseInfo ParseCourseFromSheet(DataTable sheet)
        {
            try
            {
                string sheetName = sheet.TableName;
                int year = ExtractYear(sheet);
                string quarter = ExtractQuarter(sheet);
                string deptCode = ExtractDepartmentCode(sheet);

                if (string.IsNullOrEmpty(deptCode)) return null;

                int semester = 0;
                if (sheet.ExtendedProperties.ContainsKey("Semester") &&
                    int.TryParse(sheet.ExtendedProperties["Semester"]?.ToString(), out int s))
                    semester = s;

                string yearDisplay = semester > 0
                    ? $"Sem {semester} (Year {year})"
                    : $"Year {year}";

                // Use admin-assigned display name if available, else build from dept + semester
                string customName = sheet.ExtendedProperties["DisplayName"]?.ToString();
                string courseName = !string.IsNullOrWhiteSpace(customName)
                    ? customName
                    : semester > 0
                        ? $"{GetDepartmentName(deptCode)} — Sem {semester}"
                        : $"{GetDepartmentName(deptCode)} — Year {year}";

                // ── Quarter labels shown on the course card ────────────────────
                // "Uploaded" quarter = the quarter tag embedded in the sheet's
                //   ExtendedProperties["Quarter"] (set from the Excel header on import,
                //   then updated each time the system advances the sheet).
                // "Current" quarter = whichever quarter today's date falls in.
                string liveQ = AcademicCycleService.CurrentQuarter();
                string uploadedQ = quarter; // already extracted above

                // Format: "Feb-Apr 2026"
                int calYear = DateTime.Now.Year;
                // Nov-Jan spans two calendar years — the year shown is the start year
                if (uploadedQ == "Nov-Jan" && DateTime.Now.Month <= 1) calYear--;

                string uploadedLabel = $"{uploadedQ} {calYear}";
                string liveLabel = $"{liveQ} {DateTime.Now.Year}";

                // Get original import date if available
                string importedOnText = "";
                if (_cycleService != null)
                {
                    DateTime imp = _cycleService.GetOriginalImportDate(sheetName);
                    if (imp != DateTime.MinValue)
                        importedOnText = $"  ·  Uploaded {imp:dd MMM yyyy}";
                }

                var courseInfo = new CourseInfo
                {
                    SheetName = sheetName,
                    DepartmentCode = deptCode,
                    Year = year,
                    Semester = semester,
                    Quarter = quarter,
                    CourseName = courseName,
                    YearDisplay = yearDisplay,
                    QuarterDisplay = FormatQuarter(quarter),
                    DataTable = sheet,
                    OriginalQuarterLabel = $"📂 {uploadedLabel}{importedOnText}",
                    CurrentQuarterLabel = $"📅 Now: {liveLabel}",
                    IsOnOriginalQuarter = uploadedQ == liveQ,
                };

                CalculateCourseStatistics(courseInfo);
                return courseInfo;
            }
            catch
            {
                return null;
            }
        }

        private void CalculateCourseStatistics(CourseInfo course)
        {
            if (course.DataTable == null) return;

            course.LastUpdated = DateTime.Now.ToString("dd MMM yyyy");

            DataColumn ColFind(params string[] keywords) =>
                course.DataTable.Columns.Cast<DataColumn>()
                    .Where(c => !c.ColumnName.StartsWith("_"))
                    .FirstOrDefault(c => keywords.All(k =>
                        c.ColumnName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

            decimal SafeDec(DataRow r, DataColumn c)
            {
                if (c == null) return 0m;
                string raw = r[c]?.ToString()?.Trim() ?? "";
                raw = raw.Replace("₹", "").Replace(",", "");
                return decimal.TryParse(raw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal v) ? v : 0m;
            }

            var nameCol = ColFind("name");
            var prevPendingCol = ColFind("previous", "pending")
                              ?? ColFind("previous")
                              ?? ColFind("pending");
            var quarterlyCol = ColFind("quarterly") ?? ColFind("installment");
            var totalFeesCol = ColFind("total") ?? ColFind("fees");

            var studentRows = course.DataTable.Rows.Cast<DataRow>()
                .Where(r =>
                {
                    // Skip archived rows
                    if (course.DataTable.Columns.Contains("_Archived") &&
                        r["_Archived"]?.ToString() == "1") return false;

                    if (nameCol == null) return true;
                    string nm = r[nameCol]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(nm)) return false;
                    if (nm.Equals("Name", StringComparison.OrdinalIgnoreCase)) return false;
                    if (nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) return false;
                    if (nm.Length > 60 || nm.Contains(":-") ||
                        nm.Contains("Per Day") || nm.Contains("deposited")) return false;
                    return true;
                })
                .ToList();

            course.TotalStudents = studentRows.Count;

            decimal totalPending = 0m;
            int pendingCount = 0;

            foreach (DataRow row in studentRows)
            {
                decimal prevPend = SafeDec(row, prevPendingCol);
                decimal quarterly = SafeDec(row, quarterlyCol);

                if (quarterly == 0m && quarterlyCol == null)
                    quarterly = SafeDec(row, totalFeesCol);

                decimal due = prevPend + quarterly;

                if (due > 0)
                {
                    totalPending += due;
                    pendingCount++;
                }
            }

            course.PendingAmount = totalPending;
            course.PendingStudents = pendingCount;
            course.PaidStudents = course.TotalStudents - pendingCount;
            course.HasPendingFees = pendingCount > 0;

            if (course.TotalStudents == 0)
            {
                course.FileStatus = "No Data";
                course.StatusColor = "#9E9E9E";
            }
            else if (course.PendingStudents == 0)
            {
                course.FileStatus = "All Paid";
                course.StatusColor = "#4CAF50";
            }
            else
            {
                course.FileStatus = "Active";
                course.StatusColor = "#2196F3";
            }

            var dept = Departments.FirstOrDefault(d => d.Code == course.DepartmentCode);
            course.CanPromote = dept != null && course.Year < dept.Years;

            course.ColorBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(dept?.Color ?? "#2196F3"));
        }

        private void CalculateDashboardStatistics()
        {
            TotalCourses = AllCourses.Count;
            TotalStudents = AllCourses.Sum(c => c.TotalStudents);
            TotalPendingStudents = AllCourses.Sum(c => c.PendingStudents);
            TotalPendingAmount = AllCourses.Sum(c => c.PendingAmount);
            ActiveQuarters = AllCourses.Select(c => c.Quarter).Distinct().Count();

            DepartmentSummary = $"{TotalCourses} courses • {TotalStudents} students • {ActiveQuarters} active quarters";
        }

        // ==========================
        // FILTERING
        // ==========================

        private void ApplyFilters()
        {
            FilteredCourses.Clear();

            var filtered = AllCourses.AsEnumerable();

            if (SelectedYearFilter != "All Years")
            {
                int yearNum = int.Parse(SelectedYearFilter.Replace("Year ", ""));
                filtered = filtered.Where(c => c.Year == yearNum);
            }

            if (SelectedQuarterFilter != "All Quarters")
            {
                filtered = filtered.Where(c => c.Quarter.Replace("-", "") == SelectedQuarterFilter.Replace("-", ""));
            }

            if (SelectedStatusFilter == "Has Pending")
                filtered = filtered.Where(c => c.HasPendingFees);
            else if (SelectedStatusFilter == "Fully Paid")
                filtered = filtered.Where(c => !c.HasPendingFees && c.TotalStudents > 0);
            else if (SelectedStatusFilter == "No Data")
                filtered = filtered.Where(c => c.TotalStudents == 0);

            if (!string.IsNullOrWhiteSpace(GlobalSearchText))
            {
                filtered = filtered.Where(c =>
                    c.CourseName.Contains(GlobalSearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.YearDisplay.Contains(GlobalSearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.QuarterDisplay.Contains(GlobalSearchText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var course in filtered)
                FilteredCourses.Add(course);
        }

        // ==========================
        // COURSE ACTIONS
        // ==========================

        [RelayCommand]
        public void ViewCourseData(CourseInfo course)
        {
            if (course == null || course.DataTable == null) return;

            CurrentCourse = course;
            _originalData = course.DataTable;
            _currentSheetName = course.SheetName;

            CsvTableView = _originalData.DefaultView;
            CsvTableView.RowFilter = string.Empty;

            Columns.Clear();
            foreach (DataColumn col in _originalData.Columns)
                Columns.Add(col.ColumnName);

            SelectedColumn = Columns.FirstOrDefault();
            SearchText = string.Empty;
            CurrentViewTitle = course.CourseName + " - " + course.QuarterDisplay;

            IsShowingSnapshot = false;
            SnapshotBanner = "";
            OriginalFileAddedBadge = "";
            CurrentQuarterBadge = "";

            IsDepartmentViewMode = false;
            IsDepartmentManagementMode = false;
            IsDataViewMode = true;

            UpdateRowCountDisplay();
            BuildStudentCards();
        }

        // ==========================
        // HISTORY COMMANDS
        // ==========================

        [RelayCommand]
        public void OpenHistory(object parameter)
        {
            CourseInfo course = parameter as CourseInfo ?? CurrentCourse;
            if (course?.DataTable == null || Timeline == null) return;

            if (!IsDataViewMode || CurrentCourse != course)
                ViewCourseData(course);

            if (_cycleService != null)
            {
                DateTime importedOn = _cycleService.GetOriginalImportDate(course.DataTable.TableName);
                OriginalFileAddedBadge = importedOn == DateTime.MinValue
                    ? ""
                    : $"Added {importedOn:dd MMM yyyy}";

                string curQ = AcademicCycleService.CurrentQuarter();
                int sem = course.Semester > 0 ? course.Semester : 1;
                CurrentQuarterBadge = $"{curQ} {DateTime.Now.Year}  ·  Sem {sem}";
            }

            Timeline.Open(course.DataTable, course.CourseName);
        }

        [RelayCommand]
        public void CloseHistory()
        {
            Timeline?.Close();
            RestoreLiveData();
        }

        private void SwapToSnapshot(DataTable snapshot)
        {
            CsvTableView = snapshot.DefaultView;
            CsvTableView.RowFilter = string.Empty;
            BuildStudentCardsFromView(CsvTableView);
        }

        private void RestoreLiveData()
        {
            if (_originalData == null) return;
            CsvTableView = _originalData.DefaultView;
            CsvTableView.RowFilter = string.Empty;
            IsShowingSnapshot = false;
            SnapshotBanner = "";
            BuildStudentCards();
        }

        // ==========================
        // BUILD STUDENT CARD ROWS
        // ==========================

        private void BuildStudentCards()
        {
            BuildStudentCardsFromView(CsvTableView ?? _originalData?.DefaultView);
        }

        private void BuildStudentCardsFromView(DataView view)
        {
            StudentCardRows.Clear();
            if (view == null) return;

            var table = view.Table;
            if (table == null) return;

            DataColumn ColFind(params string[] keywords) =>
                table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => keywords.All(k =>
                        c.ColumnName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0));

            decimal SafeDec(DataRow r, DataColumn c)
            {
                if (c == null) return 0m;
                string raw = r[c]?.ToString()?.Trim() ?? "";
                raw = raw.Replace("₹", "").Replace(",", "");
                return decimal.TryParse(raw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal v) ? v : 0m;
            }

            var nameCol = ColFind("name");
            var fatherCol = ColFind("father");
            var categoryCol = ColFind("category");
            var phoneCol = ColFind("phone") ?? ColFind("contact") ?? ColFind("mobile");
            var quarterlyCol = ColFind("quarterly") ?? ColFind("installment") ?? ColFind("fees");
            var prevPendCol = ColFind("previous", "pending") ?? ColFind("pending");

            var stationaryCol = ColFind("stationary");
            var welfareCol = ColFind("welfare");
            var studentActCol = ColFind("student", "activ");
            var institutionalCol = ColFind("institutional") ?? ColFind("refundable");
            var insuranceCol = ColFind("insurance") ?? ColFind("comprehensive");
            var redCrossCol = ColFind("red", "cross");
            var hostelCol = ColFind("hostel");

            int serial = 1;

            foreach (System.Data.DataRowView drv in view)
            {
                DataRow row = drv.Row;

                // Skip archived rows
                if (table.Columns.Contains("_Archived") &&
                    row["_Archived"]?.ToString() == "1") continue;

                string nm = nameCol != null ? row[nameCol]?.ToString()?.Trim() ?? "" : "";
                if (string.IsNullOrEmpty(nm)) continue;
                if (nm.Equals("Name", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.StartsWith("Note", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;

                decimal quarterly = SafeDec(row, quarterlyCol);
                decimal prevPend = SafeDec(row, prevPendCol);
                decimal totalDue = quarterly + prevPend;

                decimal stationary = SafeDec(row, stationaryCol);
                decimal welfare = SafeDec(row, welfareCol);
                decimal studentAct = SafeDec(row, studentActCol);
                decimal institutional = SafeDec(row, institutionalCol);
                decimal insurance = SafeDec(row, insuranceCol);
                decimal redCross = SafeDec(row, redCrossCol);
                decimal hostel = SafeDec(row, hostelCol);

                string cat = categoryCol != null ? row[categoryCol]?.ToString()?.Trim() ?? "" : "";
                cat = cat.ToUpper() switch
                {
                    "SC" => "SC",
                    "ST" => "ST",
                    "OBC" => "OBC",
                    "GEN" => "GEN",
                    "GENERAL" => "GEN",
                    "GEN FW" => "GEN FW",
                    "BC" => "BC",
                    _ => cat.ToUpper()
                };

                StudentCardRows.Add(new StudentCardRow
                {
                    SerialNumber = serial.ToString(),
                    Name = nm,
                    FatherName = fatherCol != null ? row[fatherCol]?.ToString()?.Trim() ?? "–" : "–",
                    PhoneNumber = phoneCol != null ? row[phoneCol]?.ToString()?.Trim() ?? "" : "",
                    Category = cat,
                    QuarterlyFee = quarterly,
                    PreviousPending = prevPend,
                    TotalDue = totalDue,
                    Stationary = stationary,
                    DevelopmentWelfare = welfare,
                    StudentActivities = studentAct,
                    InstitutionalSecurity = institutional,
                    ComprehensiveInsurance = insurance,
                    RedCrossFund = redCross,
                    Hostel = hostel,
                    SourceRow = drv
                });

                serial++;
            }

            RefreshDataViewStats();
        }

        private void RefreshDataViewStats()
        {
            DataViewTotalStudents = StudentCardRows.Count;
            DataViewPendingStudents = StudentCardRows.Count(r => r.TotalDue > 0);
            DataViewPaidStudents = StudentCardRows.Count(r => r.TotalDue <= 0);
            DataViewPendingAmount = StudentCardRows.Sum(r => r.TotalDue);
        }

        // ==========================
        // FEE COLLECTION
        // ==========================

        [RelayCommand]
        public void CollectFeeForRow(StudentCardRow card)
        {
            if (card == null) return;
            var feeView = App.Current.Services
                .GetRequiredService<SchoolFeeSystem.Presentation.Views.FeeCollectionView>();
            if (feeView.DataContext is FeeCollectionViewModel feeVm &&
                !string.IsNullOrEmpty(_currentSheetName))
            {
                string displayName = _csvService.GetSheetDisplayNames()
                    .FirstOrDefault(d => _csvService.GetSheetNameFromDisplay(d) == _currentSheetName);
                if (!string.IsNullOrEmpty(displayName))
                    feeVm.SelectedSheet = displayName;
            }
            System.Windows.Application.Current.MainWindow.Content = feeView;
        }

        // ==========================
        // COURSE RENAME
        // ==========================

        [RelayCommand]
        public void RenameCourse(CourseInfo course)
        {
            if (course == null) return;

            var dialog = new RenameDialog(course.CourseName);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName?.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            // Persist in CsvDataService — GetSheetDisplayNames() now returns newName
            // for this table, so FeeCollection, Reports, Scholarship, Fine Management
            // all pick it up automatically on next load.
            _csvService.RenameSheet(course.SheetName, newName);

            // Update the local card immediately so the UI reflects the change now
            course.CourseName = newName;
            course.DataTable.ExtendedProperties["DisplayName"] = newName;

            // Refresh the course card header if we are in data-view for this course
            if (CurrentCourse == course)
                CurrentViewTitle = newName + " - " + course.QuarterDisplay;

            // Broadcast to FeeCollection, Reports, etc. so they rebuild their lists
            App.RaiseFeeDataChanged();

            MessageBox.Show(
                $"Course renamed to:\n\"{newName}\"\n\n" +
                "The new name will appear in Fee Collection, Reports, Scholarship and Fine Management.",
                "Renamed Successfully",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ==========================
        // STUDENT REMOVE / ARCHIVE
        // ==========================

        [RelayCommand]
        public void RemoveStudentCard(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var result = MessageBox.Show(
                $"Permanently remove student:\n\n\"{card.Name}\"\n\n" +
                "This cannot be undone. Are you sure?",
                "Confirm Remove Student",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _csvService.RemoveStudentRow(card.SourceRow.Row);
                StudentCardRows.Remove(card);
                RefreshDataViewStats();
                if (CurrentCourse != null)
                    CalculateCourseStatistics(CurrentCourse);
                App.RaiseFeeDataChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing student:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ArchiveStudentCard(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var result = MessageBox.Show(
                $"Archive student:\n\n\"{card.Name}\"\n\n" +
                "They will be hidden from all views but their data is preserved.\nContinue?",
                "Confirm Archive Student",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _csvService.ArchiveStudentRow(card.SourceRow.Row);
                StudentCardRows.Remove(card);
                RefreshDataViewStats();
                if (CurrentCourse != null)
                    CalculateCourseStatistics(CurrentCourse);
                App.RaiseFeeDataChanged();

                MessageBox.Show($"\"{card.Name}\" has been archived.",
                    "Archived", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error archiving student:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================
        // STUDENT FIELD EDITING
        // ==========================

        [RelayCommand]
        public void EditStudentName(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var dialog = new RenameDialog(card.Name, "Edit Student Name");
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName?.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            var table = card.SourceRow.Row.Table;
            var nameCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase));

            if (nameCol == null)
            {
                MessageBox.Show("Could not find the Name column.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            card.SourceRow.Row[nameCol] = newName;
            card.Name = newName;
            App.RaiseFeeDataChanged();
        }

        [RelayCommand]
        public void EditStudentFather(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var dialog = new RenameDialog(card.FatherName, "Edit Father's Name");
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newVal = dialog.NewName?.Trim();
            if (string.IsNullOrWhiteSpace(newVal)) return;

            var table = card.SourceRow.Row.Table;
            var col = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.IndexOf("father", StringComparison.OrdinalIgnoreCase) >= 0);

            if (col == null) { MessageBox.Show("Father column not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            card.SourceRow.Row[col] = newVal;
            card.FatherName = newVal;
            App.RaiseFeeDataChanged();
        }

        [RelayCommand]
        public void EditStudentPhone(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var dialog = new RenameDialog(card.PhoneNumber, "Edit Phone Number");
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newVal = dialog.NewName?.Trim() ?? "";

            var table = card.SourceRow.Row.Table;

            // Try to find an existing phone/contact/mobile column
            var col = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c =>
                    c.ColumnName.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.ColumnName.IndexOf("contact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.ColumnName.IndexOf("mobile", StringComparison.OrdinalIgnoreCase) >= 0);

            // ── If no phone column exists yet, silently create one ───────────
            if (col == null)
            {
                col = table.Columns.Add("Phone No.", typeof(string));

                // Back-fill all existing rows with an empty string
                foreach (DataRow r in table.Rows)
                    if (r.RowState != DataRowState.Deleted)
                        r[col] = "";
            }
            // ────────────────────────────────────────────────────────────────

            card.SourceRow.Row[col] = newVal;
            card.PhoneNumber = newVal;

            // Auto-save so the phone number persists to disk immediately.
            // The user does not need to press Export / Save manually.
            try { _csvService.SaveFile(); }
            catch { /* best-effort; data is already updated in memory */ }

            App.RaiseFeeDataChanged();
        }

        [RelayCommand]
        public void EditStudentCategory(StudentCardRow card)
        {
            if (card?.SourceRow?.Row == null) return;

            var dialog = new RenameDialog(card.Category, "Edit Category (e.g. GEN, OBC, SC, ST, BC)");
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newVal = dialog.NewName?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(newVal)) return;

            var table = card.SourceRow.Row.Table;
            var col = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0);

            if (col == null) { MessageBox.Show("Category column not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            card.SourceRow.Row[col] = newVal;
            card.Category = newVal;
            App.RaiseFeeDataChanged();
        }

        // ==========================
        // PROMOTE / REPORT
        // ==========================

        [RelayCommand]
        public void PromoteCourse(CourseInfo course)
        {
            if (course == null || !course.CanPromote) return;

            var result = MessageBox.Show(
                $"Promote all students in:\n\n{course.CourseName}\n{course.QuarterDisplay}\n\n" +
                $"From Year {course.Year} to Year {course.Year + 1}?",
                "Confirm Promotion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool isLastYear = course.Year == Departments.FirstOrDefault(d => d.Code == course.DepartmentCode)?.Years;
                    _csvService.PromoteStudentsToNextYear(course.DepartmentCode, course.Year, isLastYear);

                    MessageBox.Show("Students promoted successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadDepartmentCourses();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error promoting students: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void GenerateReport(CourseInfo course)
        {
            if (course == null || course.DataTable == null) return;

            try
            {
                string fileName = $"{course.CourseName}_{course.QuarterDisplay}_{DateTime.Now:yyyyMMdd}.pdf";
                string filePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SchoolFeeReports", fileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
                _pdfReportService.GenerateCourseReport(course.DataTable, filePath, course.CourseName, course.QuarterDisplay);

                MessageBox.Show($"Report generated!\n\nSaved to: {filePath}", "Report Generated",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================
        // DATA VIEW COMMANDS
        // ==========================

        [RelayCommand]
        public void ShowAllRows()
        {
            if (CsvTableView == null) return;
            CsvTableView.RowFilter = string.Empty;
            UpdateRowCountDisplay();
            BuildStudentCards();
        }

        [RelayCommand]
        public void ExportCurrentSheet()
        {
            if (_originalData == null || _originalData.Rows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string safeName = string.Concat(CurrentViewTitle
                    .Split(System.IO.Path.GetInvalidFileNameChars()))
                    .Replace(" ", "_").Replace("•", "").Replace("🌸", "").Replace("🍂", "").Replace("❄️", "").Replace("☀️", "");
                string fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SchoolFeeExports");
                System.IO.Directory.CreateDirectory(folder);
                string filePath = System.IO.Path.Combine(folder, fileName);

                using (var workbook = new ClosedXML.Excel.XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Students");

                    var visibleCols = _originalData.Columns.Cast<System.Data.DataColumn>()
                        .Where(c => !c.ColumnName.StartsWith("_") &&
                                    !c.ColumnName.Equals("Sr No.", StringComparison.OrdinalIgnoreCase) &&
                                    !c.ColumnName.Equals("Sr No", StringComparison.OrdinalIgnoreCase) &&
                                    !c.ColumnName.Equals("Sr.", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    ws.Cell(1, 1).Value = "Sr No.";
                    for (int ci = 0; ci < visibleCols.Count; ci++)
                        ws.Cell(1, ci + 2).Value = visibleCols[ci].ColumnName;

                    var headerRange = ws.Range(1, 1, 1, visibleCols.Count + 1);
                    headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1976D2");
                    headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    var nameCol = _originalData.Columns.Cast<System.Data.DataColumn>()
                        .FirstOrDefault(c => c.ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase));

                    int excelRow = 2;
                    int srNo = 1;
                    foreach (System.Data.DataRow row in _originalData.Rows)
                    {
                        // Skip archived rows in export
                        if (_originalData.Columns.Contains("_Archived") &&
                            row["_Archived"]?.ToString() == "1") continue;

                        if (nameCol != null)
                        {
                            string nm = row[nameCol]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(nm) || nm.Length > 60 ||
                                nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase) ||
                                nm.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                        }

                        ws.Cell(excelRow, 1).Value = srNo++;
                        for (int ci = 0; ci < visibleCols.Count; ci++)
                        {
                            string val = row[visibleCols[ci]]?.ToString() ?? "";
                            if (decimal.TryParse(val, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out decimal num))
                                ws.Cell(excelRow, ci + 2).Value = num;
                            else
                                ws.Cell(excelRow, ci + 2).Value = val;
                        }

                        if (excelRow % 2 == 0)
                            ws.Row(excelRow).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F5F5F5");

                        excelRow++;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(filePath);
                }

                MessageBox.Show($"Exported successfully!\n\n{filePath}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void PrintReport()
        {
            if (_originalData == null) return;
            try
            {
                string fileName = $"{_currentSheetName}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
                string filePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SchoolFeeReports", fileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
                _pdfReportService.GenerateCourseReport(_originalData, filePath, CurrentViewTitle, "Current Quarter");

                MessageBox.Show($"Report saved:\n{filePath}", "Report Generated",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Report failed: {ex.Message}", "Report Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ShowPendingOnly()
        {
            if (_originalData == null) return;
            var totalCol = _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total") && c.ColumnName.ToLower().Contains("fees"));
            var pendingCol = totalCol ??
                _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") || c.ColumnName.ToLower().Contains("balance"));
            if (pendingCol != null)
            {
                CsvTableView.RowFilter = $"[{pendingCol.ColumnName}] > 0";
                UpdateRowCountDisplay();
                BuildStudentCards();
            }
        }

        [RelayCommand]
        public void ShowPaidOnly()
        {
            if (_originalData == null) return;
            var totalCol = _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total") && c.ColumnName.ToLower().Contains("fees"));
            var pendingCol = totalCol ??
                _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") || c.ColumnName.ToLower().Contains("balance"));
            if (pendingCol != null)
            {
                CsvTableView.RowFilter =
                    $"[{pendingCol.ColumnName}] = 0 OR [{pendingCol.ColumnName}] IS NULL OR " +
                    $"[{pendingCol.ColumnName}] = ''";
                UpdateRowCountDisplay();
                BuildStudentCards();
            }
        }

        [RelayCommand]
        public void ApplyCellEdit()
        {
            if (SelectedRow == null || string.IsNullOrEmpty(SelectedColumn)) return;
            try
            {
                SelectedRow[SelectedColumn] = NewValue;
                UpdateRowCountDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying edit: {ex.Message}", "Edit Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void AddRow()
        {
            if (_originalData == null) return;
            try
            {
                _originalData.Rows.Add(_originalData.NewRow());
                UpdateRowCountDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding row: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void DeleteRow()
        {
            if (SelectedRow == null) return;
            var result = MessageBox.Show(
                "Delete the selected student row? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SelectedRow.Row.Delete();
                    UpdateRowCountDisplay();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting row: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ==========================
        // MASS PROMOTION
        // ==========================

        [RelayCommand]
        public void PromoteAllYears()
        {
            var result = MessageBox.Show(
                $"⚠️ MASS PROMOTION WARNING\n\n" +
                $"This will promote ALL students in ALL years of {SelectedDepartment.Name} to the next year.\n\n" +
                $"Final year students will be moved to Pass-outs.\n\n" +
                $"This action cannot be undone automatically.\n\nAre you absolutely sure?",
                "Confirm Mass Promotion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    int promotedCount = 0;
                    foreach (var course in AllCourses.Where(c => c.CanPromote))
                    {
                        bool isLastYear = course.Year == Departments.FirstOrDefault(d => d.Code == course.DepartmentCode)?.Years;
                        _csvService.PromoteStudentsToNextYear(course.DepartmentCode, course.Year, isLastYear);
                        promotedCount++;
                    }

                    MessageBox.Show($"Successfully promoted {promotedCount} courses!", "Mass Promotion Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadDepartmentCourses();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during mass promotion: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportAllCourses()
        {
            MessageBox.Show("Export feature coming soon!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================
        // NAVIGATION
        // ==========================

        [RelayCommand]
        public void GoBackToDepartments()
        {
            IsDepartmentViewMode = true;
            IsDepartmentManagementMode = false;
            IsDataViewMode = false;
            SelectedDepartment = null;
        }

        [RelayCommand]
        public void GoBackToDashboard()
        {
            IsDepartmentViewMode = false;
            IsDepartmentManagementMode = true;
            IsDataViewMode = false;
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // ==========================
        // HELPER METHODS
        // ==========================

        private void ApplyDataViewSearch()
        {
            if (CsvTableView == null || _originalData == null) return;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                CsvTableView.RowFilter = string.Empty;
            }
            else
            {
                string search = SearchText.Replace("'", "''");
                // Search across all string columns (no "Search in" dropdown any more)
                var strCols = _originalData.Columns.Cast<DataColumn>()
                    .Where(c => c.DataType == typeof(string) && !c.ColumnName.StartsWith("_"))
                    .Select(c => $"[{c.ColumnName}] LIKE '%{search}%'");
                CsvTableView.RowFilter = string.Join(" OR ", strCols);
            }
            UpdateRowCountDisplay();
            BuildStudentCards();
        }

        private void UpdateRowCountDisplay()
        {
            if (CsvTableView == null) { RowCountDisplay = "0 students"; return; }

            int CountStudents(System.Collections.IEnumerable rows)
            {
                int count = 0;
                int nameIdx = -1;
                if (_originalData != null)
                    for (int ci = 0; ci < _originalData.Columns.Count; ci++)
                        if (_originalData.Columns[ci].ColumnName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        { nameIdx = ci; break; }

                foreach (System.Data.DataRowView drv in rows)
                {
                    if (_originalData != null && _originalData.Columns.Contains("_Archived") &&
                        drv.Row["_Archived"]?.ToString() == "1") continue;

                    if (nameIdx >= 0)
                    {
                        string name = drv.Row[nameIdx]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(name) &&
                            !name.Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("Note", StringComparison.OrdinalIgnoreCase) &&
                            name.Length <= 60)
                            count++;
                    }
                    else { count++; }
                }
                return count;
            }

            int visible = CountStudents(CsvTableView);
            int total = _originalData != null
                ? CountStudents(_originalData.DefaultView)
                : visible;

            RowCountDisplay = visible == total
                ? $"{total} students"
                : $"{visible} of {total} students";
        }

        private int ExtractYear(DataTable sheet)
        {
            if (sheet.ExtendedProperties.ContainsKey("Year") &&
                int.TryParse(sheet.ExtendedProperties["Year"]?.ToString(), out int metaYear) &&
                metaYear >= 1)
                return metaYear;

            string name = sheet.TableName.ToLower();
            for (int i = 1; i <= 4; i++)
            {
                if (name.Contains($"-{i}-") || name.Contains($"year{i}") ||
                    name.Contains($"{i}year") || name.Contains($"{i}st") ||
                    name.Contains($"{i}nd") || name.Contains($"{i}rd") ||
                    name.Contains($"{i}th"))
                    return i;
            }
            return 1;
        }

        private string ExtractQuarter(DataTable sheet)
        {
            string metaQuarter = sheet.ExtendedProperties["Quarter"]?.ToString();
            if (!string.IsNullOrEmpty(metaQuarter) && metaQuarter != "Unknown")
                return metaQuarter;

            string name = sheet.TableName.ToLower();
            if (name.Contains("augoct") || name.Contains("aug-oct") || name.Contains("aug_oct")) return "Aug-Oct";
            if (name.Contains("novjan") || name.Contains("nov-jan") || name.Contains("nov_jan")) return "Nov-Jan";
            if (name.Contains("febapr") || name.Contains("feb-apr") || name.Contains("feb_apr")) return "Feb-Apr";
            if (name.Contains("mayjun") || name.Contains("may-jun") || name.Contains("may_jun")) return "May-Jun";

            string period = sheet.ExtendedProperties["Period"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(period))
            {
                string p = period.ToUpper();
                if (p.Contains("AUG") || p.Contains("AUGUST") || p.Contains("SEP") || p.Contains("OCT")) return "Aug-Oct";
                if (p.Contains("NOV") || p.Contains("DEC") || p.Contains("JAN")) return "Nov-Jan";
                if (p.Contains("FEB") || p.Contains("FEBRUARY") || p.Contains("MAR") || p.Contains("APR") || p.Contains("APRIL")) return "Feb-Apr";
                if (p.Contains("MAY") || p.Contains("JUN") || p.Contains("JUNE")) return "May-Jun";
            }

            return "Aug-Oct";
        }

        private string ExtractDepartmentCode(DataTable sheet)
        {
            string metaDept = sheet.ExtendedProperties["Department"]?.ToString();
            if (!string.IsNullOrEmpty(metaDept) && metaDept != "General" && metaDept != "MISC")
                return metaDept;

            string name = sheet.TableName.ToUpper();
            if (name.Contains("PASSOUT") || name.Contains("PASS OUT") || name.Contains("PASS-OUT"))
                return "PASSOUT";
            if (name.Contains("MECHATRONICS"))
                return "MECHATRONICS";
            if (name.Contains("ME") || name.Contains("MECH") || name.Contains("T&D") || name.Contains("TOOL"))
                return "ME";
            if (name.Contains("EE") || name.Contains("ELECTRICAL"))
                return "EE";
            if (name.Contains("CSE") || name.Contains("CS") || name.Contains("COMPUTER"))
                return "CSE";
            if (name.Contains("MISC"))
                return "MISC";

            return metaDept ?? null;
        }

        private string GetDepartmentName(string code) =>
            Departments.FirstOrDefault(d => d.Code == code)?.Name ?? code;

        private string FormatQuarter(string quarter) => quarter switch
        {
            "Aug-Oct" => "🍂 Aug-Oct",
            "Nov-Jan" => "❄️ Nov-Jan",
            "Feb-Apr" => "🌸 Feb-Apr",
            "May-Jun" => "☀️ May-Jun",
            _ => quarter
        };

        // ==========================
        // HELPER CLASSES
        // ==========================

        public class DepartmentInfo
        {
            public string Name { get; set; }
            public string Code { get; set; }
            public int Years { get; set; }
            public string Color { get; set; }
            public string Icon { get; set; }
        }

        public class CourseInfo : ObservableObject
        {
            public string SheetName { get; set; }
            public string DepartmentCode { get; set; }
            public int Year { get; set; }
            public int Semester { get; set; }
            public string Quarter { get; set; }

            private string _courseName;
            public string CourseName
            {
                get => _courseName;
                set => SetProperty(ref _courseName, value);
            }

            public string YearDisplay { get; set; }
            public string QuarterDisplay { get; set; }
            public DataTable DataTable { get; set; }

            public int TotalStudents { get; set; }
            public int PaidStudents { get; set; }
            public int PendingStudents { get; set; }
            public decimal PendingAmount { get; set; }
            public string LastUpdated { get; set; }
            public string FileStatus { get; set; }
            public string StatusColor { get; set; }
            public bool HasPendingFees { get; set; }
            public bool CanPromote { get; set; }
            public Brush ColorBrush { get; set; }

            // ── Quarter info shown on the card (outside "View Students") ──────
            /// <summary>
            /// The quarter the file was originally uploaded for.
            /// e.g. "📂 Uploaded: Feb-Apr 2026"
            /// </summary>
            public string OriginalQuarterLabel { get; set; }

            /// <summary>
            /// The real-world quarter right now.
            /// e.g. "📅 Current: Feb-Apr 2026"
            /// </summary>
            public string CurrentQuarterLabel { get; set; }

            /// <summary>True when the file is still on the same quarter it was uploaded for.</summary>
            public bool IsOnOriginalQuarter { get; set; }
        }
    }
}