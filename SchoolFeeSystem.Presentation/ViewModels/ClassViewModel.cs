using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ClassViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private DataTable _originalData;
        private string _currentSheetName;

        // ==========================
        // DEPARTMENT & COURSE STRUCTURE
        // ==========================

        public ObservableCollection<DepartmentInfo> Departments { get; } = new();

        [ObservableProperty]
        private DepartmentInfo selectedDepartment;

        [ObservableProperty]
        private int selectedYear;

        [ObservableProperty]
        private string selectedQuarter; // "Aug-Oct", "Nov-Jan", "Feb-Apr"

        public ObservableCollection<string> AvailableYears { get; } = new();
        public ObservableCollection<string> AvailableQuarters { get; } = new();

        // ==========================
        // VIEW MODE
        // ==========================

        [ObservableProperty]
        private bool isDepartmentViewMode = true;

        [ObservableProperty]
        private bool isYearViewMode = false;

        [ObservableProperty]
        private bool isDataViewMode = false;

        // ==========================
        // TABLE DATA
        // ==========================

        [ObservableProperty]
        private DataView csvTableView;

        [ObservableProperty]
        private string currentViewTitle;

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

        // ==========================
        // STATISTICS
        // ==========================

        [ObservableProperty]
        private int totalStudents;

        [ObservableProperty]
        private decimal totalPendingFees;

        [ObservableProperty]
        private int studentsWithPendingFees;

        // ==========================
        // CONSTRUCTOR
        // ==========================

        public ClassViewModel(CsvDataService csvService)
        {
            _csvService = csvService;
            InitializeDepartments();
            LoadAvailableQuarters();
        }

        // ==========================
        // INITIALIZE DEPARTMENTS
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

        private void LoadAvailableQuarters()
        {
            AvailableQuarters.Clear();
            AvailableQuarters.Add("Aug-Oct");
            AvailableQuarters.Add("Nov-Jan");
            AvailableQuarters.Add("Feb-Apr");
            SelectedQuarter = AvailableQuarters[0];
        }

        // ==========================
        // DEPARTMENT SELECTION - FIXED TO USE OBJECT PARAMETER
        // ==========================

        [RelayCommand]
        public void SelectDepartment(object parameter)
        {
            // Convert parameter to string
            string deptCode = parameter?.ToString();
            if (string.IsNullOrEmpty(deptCode)) return;

            // Find department by code
            var dept = Departments.FirstOrDefault(d => d.Code == deptCode);
            if (dept == null) return;

            SelectedDepartment = dept;
            LoadYearsForDepartment(dept);

            // Show year selection view
            IsDepartmentViewMode = false;
            IsYearViewMode = true;
            IsDataViewMode = false;
        }

        private void LoadYearsForDepartment(DepartmentInfo dept)
        {
            AvailableYears.Clear();

            if (dept.Code == "PASSOUT")
            {
                var years = _csvService.GetAvailableAcademicYears(dept.Code);
                foreach (var year in years)
                {
                    AvailableYears.Add($"Batch {year}");
                }
            }
            else if (dept.Code == "MISC")
            {
                AvailableYears.Add("All Courses");
            }
            else
            {
                for (int i = 1; i <= dept.Years; i++)
                {
                    AvailableYears.Add($"Year {i}");
                }
            }

            if (AvailableYears.Count > 0)
                SelectedYear = 1;
        }

        // ==========================
        // YEAR SELECTION - FIXED TO USE OBJECT PARAMETER
        // ==========================

        [RelayCommand]
        public void SelectYear(object parameter)
        {
            // Convert parameter to int
            if (parameter == null) return;

            int year;
            if (parameter is int intParam)
            {
                year = intParam;
            }
            else if (int.TryParse(parameter.ToString(), out int parsedYear))
            {
                year = parsedYear;
            }
            else
            {
                return;
            }

            SelectedYear = year;
            LoadDataForSelection();
        }

        [RelayCommand]
        public void SelectQuarter(object parameter)
        {
            string quarter = parameter?.ToString();
            if (string.IsNullOrEmpty(quarter)) return;

            SelectedQuarter = quarter;
            LoadDataForSelection();
        }

        private void LoadDataForSelection()
        {
            if (SelectedDepartment == null) return;

            // Build the sheet identifier
            string sheetKey = BuildSheetKey();

            // Try to find the sheet
            var sheet = _csvService.GetSheetByFilter(
                SelectedDepartment.Code,
                SelectedYear,
                SelectedQuarter
            );

            if (sheet != null)
            {
                _currentSheetName = sheet.TableName;
                _originalData = sheet;
                CsvTableView = _originalData.DefaultView;
                CsvTableView.RowFilter = string.Empty;

                Columns.Clear();
                foreach (DataColumn col in _originalData.Columns)
                    Columns.Add(col.ColumnName);

                // Update title
                CurrentViewTitle = $"{SelectedDepartment.Name} - Year {SelectedYear} - {SelectedQuarter}";

                // Calculate statistics
                CalculateStatistics();

                // Show data view
                IsDepartmentViewMode = false;
                IsYearViewMode = false;
                IsDataViewMode = true;
            }
            else
            {
                MessageBox.Show(
                    $"No data found for:\n\n" +
                    $"Department: {SelectedDepartment.Name}\n" +
                    $"Year: {SelectedYear}\n" +
                    $"Quarter: {SelectedQuarter}\n\n" +
                    "Please upload the Excel file for this period from the Dashboard.",
                    "No Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private string BuildSheetKey()
        {
            string quarterCode = SelectedQuarter.Replace("-", "");
            return $"{SelectedDepartment.Code}-{SelectedYear}-{quarterCode}";
        }

        // ==========================
        // STATISTICS
        // ==========================

        private void CalculateStatistics()
        {
            if (_originalData == null)
            {
                TotalStudents = 0;
                TotalPendingFees = 0;
                StudentsWithPendingFees = 0;
                return;
            }

            TotalStudents = _originalData.Rows.Count;

            var pendingCol = _originalData.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("balance"));

            if (pendingCol != null)
            {
                decimal totalPending = 0;
                int countWithPending = 0;

                foreach (DataRow row in _originalData.Rows)
                {
                    string rawValue = row[pendingCol]?.ToString()?.Trim();
                    if (decimal.TryParse(rawValue?.Replace("₹", "").Replace(",", ""), out decimal pending) && pending > 0)
                    {
                        totalPending += pending;
                        countWithPending++;
                    }
                }

                TotalPendingFees = totalPending;
                StudentsWithPendingFees = countWithPending;
            }
        }

        // ==========================
        // YEAR PROGRESSION
        // ==========================

        [RelayCommand]
        public void PromoteStudentsToNextYear()
        {
            if (SelectedDepartment == null || SelectedDepartment.Code == "PASSOUT" || SelectedDepartment.Code == "MISC")
            {
                MessageBox.Show(
                    "Year progression is only available for regular courses.",
                    "Invalid Operation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"⚠️ PROMOTE STUDENTS TO NEXT YEAR\n\n" +
                $"This will move all students in:\n" +
                $"{SelectedDepartment.Name} - Year {SelectedYear}\n\n" +
                $"To Year {SelectedYear + 1}\n\n" +
                $"Students in final year will be moved to Pass-outs.\n\n" +
                "This action CANNOT be undone automatically.\n" +
                "Do you want to proceed?",
                "⚠️ Confirm Year Progression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool isLastYear = SelectedYear == SelectedDepartment.Years;

                    _csvService.PromoteStudentsToNextYear(
                        SelectedDepartment.Code,
                        SelectedYear,
                        isLastYear
                    );

                    MessageBox.Show(
                        $"✅ Students promoted successfully!\n\n" +
                        $"Year {SelectedYear} → Year {SelectedYear + 1}" +
                        (isLastYear ? " (Moved to Pass-outs)" : ""),
                        "Promotion Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    LoadDataForSelection();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"❌ Failed to promote students:\n\n{ex.Message}",
                        "Promotion Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
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
                var filtered = _originalData.Clone();

                foreach (DataRow row in _originalData.Rows)
                {
                    foreach (var item in row.ItemArray)
                    {
                        if (item != null && item.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Rows.Add(row.ItemArray);
                            break;
                        }
                    }
                }

                CsvTableView = filtered.DefaultView;
            }
            catch (Exception ex)
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

            foreach (DataColumn col in _originalData.Columns)
                newRow[col.ColumnName] = "";

            if (_originalData.Columns.Count > 0)
            {
                var firstCol = _originalData.Columns[0];
                if (firstCol.ColumnName.ToLower().Contains("sr") || firstCol.ColumnName.ToLower().Contains("no"))
                {
                    int nextNumber = _originalData.Rows.Count + 1;
                    newRow[firstCol.ColumnName] = nextNumber.ToString();
                }
            }

            _originalData.Rows.Add(newRow);
            CsvTableView = _originalData.DefaultView;
            TotalStudents = _originalData.Rows.Count;

            MessageBox.Show(
                "New row added successfully!\n\nPlease fill in the student details.",
                "Row Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void DeleteSelectedRow()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show(
                    "Please select a row to delete.",
                    "No Row Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to delete this row?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
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

                    CsvTableView = _originalData.DefaultView;
                    CalculateStatistics();

                    MessageBox.Show(
                        "Row deleted successfully!",
                        "Row Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
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
                        _csvService.RecalculateRowFees(_currentSheetName, row);
                        break;
                    }
                }

                CsvTableView = _originalData.DefaultView;
                CalculateStatistics();

                MessageBox.Show(
                    $"Cell updated successfully!\n\nColumn: {SelectedColumn}\nNew Value: {NewValue}",
                    "Cell Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                NewValue = string.Empty;
            }
            catch (Exception ex)
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
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to save changes:\n\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================
        // NAVIGATION
        // ==========================

        [RelayCommand]
        public void GoBackToDepartments()
        {
            IsDepartmentViewMode = true;
            IsYearViewMode = false;
            IsDataViewMode = false;
            SelectedDepartment = null;
        }

        [RelayCommand]
        public void GoBackToYears()
        {
            IsDepartmentViewMode = false;
            IsYearViewMode = true;
            IsDataViewMode = false;
        }

        [RelayCommand]
        public void GoBack()
        {
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
                catch (Exception ex)
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
                return;
            }

            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // ==========================
        // HELPER CLASS
        // ==========================

        public class DepartmentInfo
        {
            public string Name { get; set; }
            public string Code { get; set; }
            public int Years { get; set; }
            public string Color { get; set; }
            public string Icon { get; set; }
        }
    }
}