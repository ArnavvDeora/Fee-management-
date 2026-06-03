using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StaffDirectoryViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // All employees (master list for filtering)
        private List<Employee> _allEmployees = new();

        [ObservableProperty] private ObservableCollection<Employee> _teachingStaff = new();
        [ObservableProperty] private ObservableCollection<Employee> _nonTeachingStaff = new();
        [ObservableProperty] private ObservableCollection<Employee> _adminStaff = new();
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private string _importStatusMessage = "";

        // ── Flagged / unmatched biometric entries ─────────────────────────────
        private List<FlaggedBiometricEntry> _allFlaggedEntries = new(); // master list for filtering
        [ObservableProperty] private ObservableCollection<FlaggedBiometricEntry> _flaggedStaff = new();
        [ObservableProperty] private int _flaggedCount = 0;
        [ObservableProperty] private string _flaggedTabHeader = "Unmatched Biometrics";

        // ── Unmatched tab search ───────────────────────────────────────────────
        [ObservableProperty] private string _unmatchedSearchText = "";
        partial void OnUnmatchedSearchTextChanged(string value) => ApplyUnmatchedFilter();

        // ── Link-dialog state ─────────────────────────────────────────────────
        [ObservableProperty] private bool _isLinkDialogOpen = false;
        [ObservableProperty] private FlaggedBiometricEntry _selectedFlaggedEntry;
        [ObservableProperty] private ObservableCollection<Employee> _linkEmployeeList = new();
        [ObservableProperty] private Employee _selectedLinkTarget;
        [ObservableProperty] private string _linkSearchText = "";

        // ── Add-from-Unmatched dialog state ───────────────────────────────────
        /// <summary>True while the "Add New Employee" panel is open for an unmatched entry.</summary>
        [ObservableProperty] private bool _isAddFromUnmatchedOpen = false;

        /// <summary>The flagged entry that triggered the Add panel.</summary>
        [ObservableProperty] private FlaggedBiometricEntry _addSourceEntry;

        // Quick-add form fields
        [ObservableProperty] private string _newFirstName = "";
        [ObservableProperty] private string _newLastName = "";
        [ObservableProperty] private string _newFatherName = "NA";
        [ObservableProperty] private string _newDesignation = "";
        [ObservableProperty] private string _newDepartment = "";
        [ObservableProperty] private string _newStaffType = "Non-Teaching";
        [ObservableProperty] private decimal _newBaseSalary = 0;
        [ObservableProperty] private string _newPhoneNumber = "0000000000";
        [ObservableProperty] private DateTime _newJoiningDate = DateTime.Now;
        [ObservableProperty] private string _newGender = "Male";
        [ObservableProperty] private string _newCategory = "General";

        // Dropdown sources exposed to XAML ComboBoxes
        public List<string> StaffTypeOptions { get; } = new() { "Teaching", "Non-Teaching", "Admin", "Support" };
        public List<string> GenderOptions { get; } = new() { "Male", "Female", "Other" };
        public List<string> CategoryOptions { get; } = new() { "General", "OBC", "SC", "ST", "BC", "Other" };

        public StaffDirectoryViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            RefreshData();
        }

        public void RefreshData()
        {
            _allEmployees = _payrollService.GetAllEmployees();
            ApplyFilter();
            LoadFlaggedEntries();
        }

        private void LoadFlaggedEntries()
        {
            _allFlaggedEntries = _payrollService.GetUnresolvedFlaggedBiometrics();
            FlaggedCount = _allFlaggedEntries.Count;
            FlaggedTabHeader = FlaggedCount > 0
                ? $"⚠️ Unmatched ({FlaggedCount})"
                : "Unmatched Biometrics";
            ApplyUnmatchedFilter(); // applies current search text (or shows all)
        }

        private void ApplyUnmatchedFilter()
        {
            var q = UnmatchedSearchText?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(q))
            {
                FlaggedStaff = new ObservableCollection<FlaggedBiometricEntry>(_allFlaggedEntries);
                return;
            }
            FlaggedStaff = new ObservableCollection<FlaggedBiometricEntry>(
                _allFlaggedEntries.Where(f =>
                    (f.BiometricId?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                    (f.BiometricName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)));
        }

        [RelayCommand]
        public void SearchUnmatched() => ApplyUnmatchedFilter();

        [RelayCommand]
        public void ClearUnmatchedSearch()
        {
            UnmatchedSearchText = "";  // triggers OnUnmatchedSearchTextChanged → ApplyUnmatchedFilter
        }

        private void ApplyFilter()
        {
            var query = _allEmployees.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(e =>
                    (e.FullName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.Designation?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.Department?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.SsCode?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.BiometricId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.PhoneNumber?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true));
            }

            var all = query.ToList();

            TeachingStaff = new ObservableCollection<Employee>(all.Where(e =>
                e.StaffType?.Equals("Teaching", StringComparison.OrdinalIgnoreCase) == true));
            NonTeachingStaff = new ObservableCollection<Employee>(all.Where(e =>
                e.StaffType?.Equals("Non-Teaching", StringComparison.OrdinalIgnoreCase) == true));
            AdminStaff = new ObservableCollection<Employee>(all.Where(e =>
                e.StaffType?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true ||
                e.StaffType?.Equals("Support", StringComparison.OrdinalIgnoreCase) == true));
        }

        // ────────────────────────────────────────────────────────────
        // SEARCH
        // ────────────────────────────────────────────────────────────

        [RelayCommand]
        public void PerformSearch()
        {
            ApplyFilter();
        }

        // ────────────────────────────────────────────────────────────
        // NAVIGATION
        // ────────────────────────────────────────────────────────────

        [RelayCommand]
        public void AddNewStaff()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<AddStaffView>();
            var vm = services.GetRequiredService<AddStaffViewModel>();
            view.DataContext = vm;
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void ViewDetails(Employee employee)
        {
            if (employee == null) return;

            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<StaffDetailsView>();
            var vm = services.GetRequiredService<StaffDetailsViewModel>();
            vm.SetEmployee(employee);
            view.DataContext = vm;
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        // ────────────────────────────────────────────────────────────
        // ★  IMPORT SS MASTER  ★
        //
        // The SS Master Excel (SS_Master.xlsx) is the HR payroll file
        // you get from the company every month. Upload it HERE in the
        // Staff Directory. It will:
        //   1. Match each row to an existing employee by Name OR SsCode.
        //   2. Update SsCode, Department, Designation, BaseSalary,
        //      BankAccountNo, IfscCode, UanNumber, EsiNumber.
        //   3. Create NEW employees for rows that don't match anyone.
        //
        // SS Master columns (as seen in file — updated April 2026):
        //  0=S NO  1=CODE(SsCode)  2=BIOMETRIC CODE  3=SECTION(Dept)
        //  4=NAME  5=FATHER'S NAME  6=DESIGNATION  7=UAN  8=ESI NO
        //  9=BANK A/C NO  10=IFSC  11=BASIC SALARY
        //  12=DAYS  13=OT  14=REC
        //  (columns 15+ are payroll calculation columns, we skip them)
        // ────────────────────────────────────────────────────────────

        [RelayCommand]
        public void ImportSsMaster()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx;*.xls",
                Title = "Select SS Master Excel File"
            };

            if (dlg.ShowDialog() != true) return;

            ImportStatusMessage = "Processing SS Master...";
            int updated = 0, created = 0, skipped = 0, attendanceSaved = 0;
            var errors = new List<string>();

            // ── Month/Year detected from title row (e.g. "MONTH - JANUARY 2026") ──
            int detectedMonth = 0, detectedYear = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var stream = File.Open(dlg.FileName, FileMode.Open, FileAccess.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var dataset = reader.AsDataSet();

                // ── Load employee cache ONCE — shared across all sheets ──────────
                var existingEmployees = _payrollService.GetAllEmployees();

                // ── LOOP ALL SHEETS (Table 4, Table 5, Table 6 …) ────────────────
                for (int tableIndex = 0; tableIndex < dataset.Tables.Count; tableIndex++)
                {
                    var table = dataset.Tables[tableIndex];

                    // Find header row: must contain "CODE" and "BASIC"
                    int headerRow = -1;
                    for (int r = 0; r < Math.Min(10, table.Rows.Count); r++)
                    {
                        var rowStr = string.Join("|", table.Rows[r].ItemArray.Select(x => x?.ToString() ?? ""));
                        if (rowStr.Contains("CODE", StringComparison.OrdinalIgnoreCase) &&
                            rowStr.Contains("BASIC", StringComparison.OrdinalIgnoreCase))
                        {
                            headerRow = r;
                            break;
                        }
                    }
                    if (headerRow == -1) continue; // sheet has no valid header — skip

                    // ── Try to extract month from any cell BEFORE the header row ──
                    // Table 4 has: "SALARY REPORT … MONTH - JANUARY 2026"
                    // Tables 5 & 6 don't have it — they inherit from Table 4's detection
                    if (detectedMonth == 0)
                    {
                        for (int r = 0; r < headerRow; r++)
                        {
                            for (int c = 0; c < table.Rows[r].ItemArray.Length; c++)
                            {
                                string cellText = table.Rows[r][c]?.ToString() ?? "";
                                var m = System.Text.RegularExpressions.Regex.Match(
                                    cellText,
                                    @"MONTH\s*[-–]\s*([A-Z]+)\s+(\d{4})",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (m.Success)
                                {
                                    detectedMonth = ParseMonthName(m.Groups[1].Value);
                                    int.TryParse(m.Groups[2].Value, out detectedYear);
                                    break;
                                }
                            }
                            if (detectedMonth > 0) break;
                        }
                    }

                    int dataStartRow = headerRow + 1;

                    for (int i = dataStartRow; i < table.Rows.Count; i++)
                    {
                        try
                        {
                            var row = table.Rows[i];
                            string Get(int col) => row.ItemArray.Length > col
                                ? row[col]?.ToString()?.Trim() ?? ""
                                : "";

                            // ── Columns (updated April 2026: BIOMETRIC CODE added at col2) ──
                            // col0=SNO  col1=CODE  col2=BIOMETRIC CODE  col3=SECTION
                            // col4=NAME  col5=FATHER  col6=DESIG  col7=UAN  col8=ESI
                            // col9=BANK  col10=IFSC  col11=BASIC  col12=DAYS
                            // col13=OT   col14=REC
                            string ssCode = Get(1);
                            string biometricCode = Get(2);  // ★ NEW — Biometric device code
                            string section = Get(3);
                            string fullName = Get(4);
                            string fatherName = Get(5);
                            string desig = Get(6);
                            string uan = Get(7);
                            string esi = Get(8);
                            string bankAcc = Get(9);
                            string ifsc = Get(10);
                            string salaryStr = Get(11);
                            string daysStr = Get(12); // Days worked this month
                            string otStr = Get(13); // OT hours
                            string recStr = Get(14); // Recovery hours (late)

                            // Skip empty / total rows
                            if (string.IsNullOrWhiteSpace(fullName)) continue;
                            if (fullName.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;

                            decimal.TryParse(salaryStr, out decimal salary);
                            decimal.TryParse(daysStr, out decimal daysWorked);
                            decimal.TryParse(otStr, out decimal otHours);
                            decimal.TryParse(recStr, out decimal recHours);

                            // ── Match employee: SsCode → BiometricId → Name ──────
                            // SS Code is unique per employee — always try it first.
                            // Biometric Code is also unique — try it second.
                            // Name fallback ONLY when neither code is in the DB yet,
                            // because two different employees can share the same name
                            // (e.g. two people called RAKESH KUMAR or POONAM).
                            Employee emp = null;

                            // Step 1: exact SS Code match
                            if (!string.IsNullOrEmpty(ssCode))
                            {
                                emp = existingEmployees.FirstOrDefault(e =>
                                    e.SsCode?.Equals(ssCode, StringComparison.OrdinalIgnoreCase) == true);
                            }

                            // Step 2: exact Biometric Code match (★ NEW)
                            if (emp == null && !string.IsNullOrWhiteSpace(biometricCode))
                            {
                                emp = existingEmployees.FirstOrDefault(e =>
                                    !string.IsNullOrWhiteSpace(e.BiometricId) &&
                                    e.BiometricId.Trim().Equals(biometricCode.Trim(), StringComparison.OrdinalIgnoreCase));
                            }

                            // Step 3: name fallback — only unambiguous matches
                            if (emp == null && !string.IsNullOrWhiteSpace(fullName))
                            {
                                string normName = NormalizeName(fullName);
                                var nameMatches = existingEmployees.Where(e =>
                                    NormalizeName(e.FullName).Equals(normName, StringComparison.OrdinalIgnoreCase) &&
                                    string.IsNullOrEmpty(e.SsCode)).ToList();

                                if (nameMatches.Count == 1)
                                    emp = nameMatches[0];
                                // If 0 or 2+ matches → treat as new employee (will be created below)
                            }

                            if (emp != null)
                            {
                                // ── UPDATE employee master data ────────────────────
                                emp.SsCode = ssCode;
                                emp.Department = string.IsNullOrEmpty(section) ? emp.Department : section;
                                emp.Designation = string.IsNullOrEmpty(desig) ? emp.Designation : desig;
                                emp.FatherName = string.IsNullOrEmpty(fatherName) ? emp.FatherName : fatherName;
                                emp.UanNumber = string.IsNullOrEmpty(uan) ? emp.UanNumber : uan;
                                emp.EsiNumber = string.IsNullOrEmpty(esi) ? emp.EsiNumber : esi;
                                emp.BankAccountNo = string.IsNullOrEmpty(bankAcc) ? emp.BankAccountNo : bankAcc;
                                emp.IfscCode = string.IsNullOrEmpty(ifsc) ? emp.IfscCode : ifsc;
                                if (salary > 0) emp.BaseSalary = salary;

                                // ★ NEW — Populate BiometricId from SS Master's BIOMETRIC CODE column.
                                // This is the PRIMARY key for matching attendance files.
                                // Only overwrite if the SS Master actually has a value.
                                if (!string.IsNullOrWhiteSpace(biometricCode))
                                    emp.BiometricId = biometricCode.Trim();

                                _payrollService.UpdateEmployee(emp);
                                updated++;

                                // ── Save attendance for payroll calculation ────────
                                // If we know the month AND the row has days-worked data,
                                // store it so ProcessPayroll can reproduce the Excel result.
                                if (detectedMonth > 0 && daysWorked > 0)
                                {
                                    _payrollService.SaveSsMasterAttendance(
                                        emp.Id, detectedMonth, detectedYear,
                                        daysWorked, otHours, recHours);
                                    attendanceSaved++;
                                }
                            }
                            else
                            {
                                // ── CREATE new employee ───────────────────────────
                                var names = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                                // FIX: Make email unique so it doesn't conflict with existing "na@na.local" rows
                                string newEmpEmail = string.IsNullOrWhiteSpace(ssCode)
                                    ? $"na_{Guid.NewGuid():N}@na.local"
                                    : $"na_{ssCode.ToLower()}@na.local";
                                var newEmp = new Employee
                                {
                                    FirstName = names[0],
                                    LastName = names.Length > 1 ? names[1] : "",
                                    FatherName = string.IsNullOrWhiteSpace(fatherName) ? "NA" : fatherName,
                                    Designation = string.IsNullOrWhiteSpace(desig) ? "Unknown" : desig,
                                    Department = string.IsNullOrWhiteSpace(section) ? "General" : section,
                                    SsCode = ssCode,
                                    BiometricId = string.IsNullOrWhiteSpace(biometricCode) ? null : biometricCode.Trim(), // ★ NEW
                                    UanNumber = string.IsNullOrWhiteSpace(uan) ? "NA" : uan,
                                    EsiNumber = string.IsNullOrWhiteSpace(esi) ? "NA" : esi,
                                    BankAccountNo = string.IsNullOrWhiteSpace(bankAcc) ? "NA" : bankAcc,
                                    IfscCode = string.IsNullOrWhiteSpace(ifsc) ? "NA" : ifsc,
                                    BaseSalary = salary,
                                    StaffType = GuessStaffType(desig),
                                    JoiningDate = DateTime.Now,
                                    IsActive = true,
                                    Photo = Array.Empty<byte>(),
                                    // FIX: Required fields that were missing — caused DbUpdateException
                                    Gender = "Male",
                                    Category = "General",
                                    DateOfBirth = new DateTime(1990, 1, 1),
                                    MaritalStatus = "Unknown",
                                    Qualification = "NA",
                                    AadharNumber = "NA",
                                    PanNumber = "NA",
                                    Address = "NA",
                                    Email = newEmpEmail,
                                    PhoneNumber = "0000000000",
                                };
                                _payrollService.AddEmployee(newEmp);
                                existingEmployees.Add(newEmp);
                                created++;

                                if (detectedMonth > 0 && daysWorked > 0)
                                {
                                    _payrollService.SaveSsMasterAttendance(
                                        newEmp.Id, detectedMonth, detectedYear,
                                        daysWorked, otHours, recHours);
                                    attendanceSaved++;
                                }
                            }
                        }
                        catch (Exception rowEx)
                        {
                            errors.Add($"Sheet {tableIndex + 1} Row {i + 1}: {rowEx.Message}");
                            skipped++;
                        }
                    }
                } // ── end sheet loop ────────────────────────────────────────────

                RefreshData();

                string monthLabel = detectedMonth > 0
                    ? System.Globalization.CultureInfo.InvariantCulture
                          .DateTimeFormat.GetMonthName(detectedMonth) + " " + detectedYear
                    : "unknown";

                string summary = $"✅ SS Master imported!\n\n" +
                                 $"• Updated: {updated} employees\n" +
                                 $"• Created: {created} new employees\n" +
                                 $"• Skipped: {skipped} rows\n" +
                                 $"• Month detected: {monthLabel}\n" +
                                 $"• Attendance records saved: {attendanceSaved}\n\n" +
                                 $"➡ Go to Process Payroll → select {monthLabel} → Calculate All Salaries";

                if (errors.Count > 0)
                    summary += $"\n\n⚠️ Errors ({errors.Count}):\n" + string.Join("\n", errors.Take(5));

                ImportStatusMessage = $"Done — {updated} updated, {created} created, {skipped} skipped";
                MessageBox.Show(summary, "SS Master Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ImportStatusMessage = "Import failed.";
                MessageBox.Show($"Failed to import SS Master:\n\n{ex.Message}", "Import Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Converts month name string to 1–12 integer.</summary>
        private static int ParseMonthName(string name)
        {
            var months = new[] {
                "JANUARY","FEBRUARY","MARCH","APRIL","MAY","JUNE",
                "JULY","AUGUST","SEPTEMBER","OCTOBER","NOVEMBER","DECEMBER"
            };
            for (int i = 0; i < months.Length; i++)
                if (months[i].Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            return 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\b(Mr|Mrs|Ms|Dr|Prof)\b\.?\s*", "",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static string GuessStaffType(string designation)
        {
            if (string.IsNullOrEmpty(designation)) return "Non-Teaching";
            string d = designation.ToUpperInvariant();
            if (d.Contains("TEACHER") || d.Contains("LECTURER") || d.Contains("FACULTY") ||
                d.Contains("INSTRUCTOR") || d.Contains("PRINCIPAL"))
                return "Teaching";
            if (d.Contains("ACCOUNTANT") || d.Contains("DIRECTOR") || d.Contains("MANAGER") ||
                d.Contains("OFFICER") || d.Contains("CLERK") || d.Contains("ADMIN"))
                return "Admin";
            return "Non-Teaching";
        }

        // ══════════════════════════════════════════════════════════════
        // UNMATCHED BIOMETRICS — LINK / DISMISS
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the inline link panel for the chosen flagged entry.
        /// Pre-loads all employees into the searchable dropdown.
        /// Closes the Add panel if it was open.
        /// </summary>
        [RelayCommand]
        public void OpenLinkDialog(FlaggedBiometricEntry entry)
        {
            if (entry == null) return;

            // Close add panel if open
            IsAddFromUnmatchedOpen = false;
            AddSourceEntry = null;

            SelectedFlaggedEntry = entry;
            SelectedLinkTarget = null;
            LinkSearchText = "";

            var all = _payrollService.GetAllEmployees().OrderBy(e => e.FullName).ToList();
            LinkEmployeeList = new ObservableCollection<Employee>(all);

            IsLinkDialogOpen = true;
        }

        /// <summary>Live-filters the employee dropdown as admin types.</summary>
        [RelayCommand]
        public void FilterLinkEmployees()
        {
            var all = _payrollService.GetAllEmployees().OrderBy(e => e.FullName).ToList();

            if (string.IsNullOrWhiteSpace(LinkSearchText))
            {
                LinkEmployeeList = new ObservableCollection<Employee>(all);
                return;
            }

            string q = LinkSearchText.Trim();
            LinkEmployeeList = new ObservableCollection<Employee>(
                all.Where(e =>
                    (e.FullName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.BiometricId?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.SsCode?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.Designation?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)));
        }

        /// <summary>
        /// Writes the BioID to the chosen employee and marks the entry resolved.
        /// From this point forward, attendance imports match by BioID and the
        /// entry never appears in the Unmatched tab again.
        ///
        /// Special case — duplicate names (e.g. two "Harjit Singh"):
        ///   If the chosen employee already has a DIFFERENT BioID stored, this means
        ///   two real people share the same name but have different codes.
        ///   The admin is warned and guided: the employee's stored BioID will be
        ///   replaced with the new one, and the old BioID will appear in Unmatched
        ///   on the next import so it can be assigned to a new employee record.
        /// </summary>
        [RelayCommand]
        public void ConfirmLink()
        {
            if (SelectedFlaggedEntry == null || SelectedLinkTarget == null)
            {
                MessageBox.Show("Please select an employee from the list before confirming.",
                    "No Employee Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var emp = SelectedLinkTarget;
            var entry = SelectedFlaggedEntry;

            // Warn if overwriting an existing different BioID
            if (!string.IsNullOrWhiteSpace(emp.BiometricId) &&
                !emp.BiometricId.Equals(entry.BiometricId, StringComparison.OrdinalIgnoreCase))
            {
                var confirm = MessageBox.Show(
                    $"⚠️  '{emp.FullName}' already has Biometric ID '{emp.BiometricId}'.\n\n" +
                    $"This means TWO different people share the same name but have different codes:\n" +
                    $"  • Current employee record  →  BioID '{emp.BiometricId}'\n" +
                    $"  • This unmatched entry     →  BioID '{entry.BiometricId}'\n\n" +
                    $"If you click YES:\n" +
                    $"  ✔ This employee's BioID will be updated to '{entry.BiometricId}'\n" +
                    $"  ✔ BioID '{emp.BiometricId}' will appear in Unmatched on the next import\n" +
                    $"  ✔ You can then use 'Add as New Employee' to create a separate record for BioID '{emp.BiometricId}'\n\n" +
                    $"If you click NO, use 'Add as New Employee' instead to create a brand-new record for BioID '{entry.BiometricId}'.",
                    "Two people with the same name — which action?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
            }

            try
            {
                // 1. Write BioID to the employee record
                emp.BiometricId = entry.BiometricId;
                _payrollService.UpdateEmployee(emp);

                // 2. Mark the flagged entry as resolved in DB
                _payrollService.ResolveFlaggedBiometric(entry.Id, emp.Id);

                IsLinkDialogOpen = false;
                SelectedFlaggedEntry = null;
                SelectedLinkTarget = null;

                MessageBox.Show(
                    $"✅  Linked successfully!\n\n" +
                    $"Biometric ID  '{entry.BiometricId}'\n" +
                    $"➜  Assigned to  '{emp.FullName}'\n\n" +
                    $"All future attendance imports will automatically recognise this person.",
                    "Link Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                RefreshData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CancelLink()
        {
            IsLinkDialogOpen = false;
            SelectedFlaggedEntry = null;
            SelectedLinkTarget = null;
        }

        /// <summary>
        /// Permanently dismisses a flagged entry — use for ex-employees,
        /// trainees, or anyone who is deliberately NOT in the SS Master.
        /// </summary>
        [RelayCommand]
        public void DismissFlagged(FlaggedBiometricEntry entry)
        {
            if (entry == null) return;

            var result = MessageBox.Show(
                $"Dismiss '{entry.BiometricName}'  (BioID: {entry.BiometricId})?\n\n" +
                $"This person will no longer appear in the Unmatched list.\n" +
                $"Use this for ex-employees, trainees, or contract staff not on payroll.",
                "Dismiss Entry?", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _payrollService.ResolveFlaggedBiometric(entry.Id, null); // null = dismissed, not linked
            RefreshData();
        }

        // ══════════════════════════════════════════════════════════════
        // ★ NEW — ADD FROM UNMATCHED
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the quick-add form pre-populated with data from the flagged entry.
        /// The Biometric ID is already known from the attendance file, so it is
        /// pre-filled and locked — no manual linking step needed after saving.
        /// Closes the Link panel if it was open.
        /// </summary>
        [RelayCommand]
        public void OpenAddFromUnmatched(FlaggedBiometricEntry entry)
        {
            if (entry == null) return;

            // Close link panel if open
            IsLinkDialogOpen = false;
            SelectedFlaggedEntry = null;
            SelectedLinkTarget = null;

            AddSourceEntry = entry;

            // Pre-fill name from biometric file — strip title prefixes then split
            string rawName = System.Text.RegularExpressions.Regex.Replace(
                entry.BiometricName ?? "",
                @"^(Mr\.?|Mrs\.?|Ms\.?|Dr\.?)\s*",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            var parts = rawName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            NewFirstName = parts.Length > 0 ? parts[0] : rawName;
            NewLastName = parts.Length > 1 ? parts[1] : "";

            // Reset everything else to clean defaults
            NewFatherName = "NA";
            NewDesignation = "";
            NewDepartment = "";
            NewStaffType = "Non-Teaching";
            NewBaseSalary = 0;
            NewPhoneNumber = "0000000000";
            NewJoiningDate = DateTime.Now;
            NewGender = "Male";
            NewCategory = "General";

            IsAddFromUnmatchedOpen = true;
        }

        /// <summary>Closes the add panel without saving anything.</summary>
        [RelayCommand]
        public void CancelAddFromUnmatched()
        {
            IsAddFromUnmatchedOpen = false;
            AddSourceEntry = null;
        }

        /// <summary>
        /// Validates the quick-add form, creates the Employee in the DB with the
        /// BiometricId already set, resolves the flagged entry, then refreshes.
        /// The admin can complete remaining details via Staff Details later.
        /// </summary>
        [RelayCommand]
        public void ConfirmAddFromUnmatched()
        {
            // ── Validation ───────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(NewFirstName))
            {
                MessageBox.Show("First Name is required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(NewDesignation))
            {
                MessageBox.Show("Designation is required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(NewDepartment))
            {
                MessageBox.Show("Department is required.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (NewBaseSalary < 0)
            {
                MessageBox.Show("Base Salary cannot be negative.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // ── FIX: Guard against duplicate BiometricId BEFORE hitting the DB unique constraint ──
                string bioIdToAssign = AddSourceEntry?.BiometricId;
                if (!string.IsNullOrWhiteSpace(bioIdToAssign))
                {
                    var duplicate = _payrollService.GetAllEmployees()
                        .FirstOrDefault(e => e.BiometricId != null &&
                            e.BiometricId.Equals(bioIdToAssign, StringComparison.OrdinalIgnoreCase));
                    if (duplicate != null)
                    {
                        MessageBox.Show(
                            $"An employee with Biometric ID '{bioIdToAssign}' already exists:\n\n" +
                            $"Name: {duplicate.FullName}\n\n" +
                            $"Use 'Link to Employee' to connect this attendance entry to the existing record.",
                            "Duplicate Biometric ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // ── FIX: Make Email unique per employee to avoid unique-index violations ──
                string uniqueEmail = string.IsNullOrWhiteSpace(bioIdToAssign)
                    ? $"na_{Guid.NewGuid():N}@na.local"
                    : $"na_{bioIdToAssign.ToLower()}@na.local";

                var newEmp = new Employee
                {
                    FirstName = NewFirstName.Trim(),
                    LastName = NewLastName?.Trim() ?? "",
                    FatherName = string.IsNullOrWhiteSpace(NewFatherName) ? "NA" : NewFatherName.Trim(),
                    Gender = NewGender ?? "Male",
                    Category = NewCategory ?? "General",
                    Designation = NewDesignation.Trim(),
                    Department = NewDepartment.Trim(),
                    StaffType = NewStaffType ?? "Non-Teaching",
                    BaseSalary = NewBaseSalary,
                    PhoneNumber = string.IsNullOrWhiteSpace(NewPhoneNumber) ? "0000000000" : NewPhoneNumber.Trim(),
                    JoiningDate = NewJoiningDate,
                    BiometricId = bioIdToAssign,

                    // Required defaults — admin can complete these later via Staff Details
                    DateOfBirth = new DateTime(1990, 1, 1),
                    MaritalStatus = "Unknown",
                    Qualification = "NA",
                    AadharNumber = "NA",
                    PanNumber = "NA",
                    Address = "NA",
                    Email = uniqueEmail,
                    BankAccountNo = "NA",
                    IfscCode = "NA",
                    UanNumber = "NA",
                    EsiNumber = "NA",
                    SsCode = "",
                    Photo = Array.Empty<byte>(),
                    IsActive = true
                };

                _payrollService.AddEmployee(newEmp);

                // Resolve the flagged entry so it disappears from the Unmatched tab
                if (AddSourceEntry != null)
                    _payrollService.ResolveFlaggedBiometric(AddSourceEntry.Id, newEmp.Id);

                string empName = newEmp.FullName;
                string bioId = bioIdToAssign ?? "";

                // Clear state before showing the success dialog
                IsAddFromUnmatchedOpen = false;
                AddSourceEntry = null;

                MessageBox.Show(
                    $"✅  Employee added successfully!\n\n" +
                    $"Name        : {empName}\n" +
                    $"Biometric ID: {bioId}\n" +
                    $"Department  : {newEmp.Department}\n\n" +
                    $"They have been removed from the Unmatched list and future\n" +
                    $"attendance imports will match them automatically.\n\n" +
                    $"You can fill in the remaining details (Aadhar, bank info, etc.)\n" +
                    $"via Staff Directory → View Details.",
                    "Employee Added", MessageBoxButton.OK, MessageBoxImage.Information);

                RefreshData();
            }
            catch (Exception ex)
            {
                // Unwrap EF Core DbUpdateException to show the real constraint/SQL detail
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                string detail = inner == ex ? ex.Message : $"{ex.Message}\n\nCause: {inner.Message}";
                MessageBox.Show($"Error adding employee:\n{detail}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}