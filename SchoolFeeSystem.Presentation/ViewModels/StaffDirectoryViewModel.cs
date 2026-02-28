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

        public StaffDirectoryViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            RefreshData();
        }

        public void RefreshData()
        {
            _allEmployees = _payrollService.GetAllEmployees();
            ApplyFilter();
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
        // SS Master columns (as seen in file):
        //  1=S NO  2=CODE(SsCode)  3=SECTION(Dept)  4=NAME
        //  5=FATHER'S NAME  6=DESIGNATION  7=UAN  8=ESI NO
        //  9=BANK A/C NO  10=IFSC  11=BASIC SALARY
        //  (columns 12-19 are payroll calculation columns, we skip them)
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

                            // ── Columns 1–11 are IDENTICAL in all 3 sheets ────────
                            // col0=SNO  col1=CODE  col2=SECTION  col3=NAME
                            // col4=FATHER  col5=DESIG  col6=UAN  col7=ESI
                            // col8=BANK  col9=IFSC  col10=BASIC  col11=DAYS
                            // col12=OT   col13=REC
                            string ssCode = Get(1);
                            string section = Get(2);
                            string fullName = Get(3);
                            string fatherName = Get(4);
                            string desig = Get(5);
                            string uan = Get(6);
                            string esi = Get(7);
                            string bankAcc = Get(8);
                            string ifsc = Get(9);
                            string salaryStr = Get(10);
                            string daysStr = Get(11); // Days worked this month
                            string otStr = Get(12); // OT hours
                            string recStr = Get(13); // Recovery hours (late)

                            // Skip empty / total rows
                            if (string.IsNullOrWhiteSpace(fullName)) continue;
                            if (fullName.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;

                            decimal.TryParse(salaryStr, out decimal salary);
                            decimal.TryParse(daysStr, out decimal daysWorked);
                            decimal.TryParse(otStr, out decimal otHours);
                            decimal.TryParse(recStr, out decimal recHours);

                            // ── Match employee by SsCode (primary key) ────────────
                            // SS Code is unique per employee — always use it when present.
                            // Name fallback ONLY when the Excel row itself has no SS Code,
                            // because two different employees can share the same name
                            // (e.g. two people called RAKESH KUMAR or POONAM).
                            Employee emp = null;
                            if (!string.IsNullOrEmpty(ssCode))
                            {
                                // Step 1: exact SS Code match in DB
                                emp = existingEmployees.FirstOrDefault(e =>
                                    e.SsCode?.Equals(ssCode, StringComparison.OrdinalIgnoreCase) == true);

                                // Step 2: SS Code not yet in DB — find the one employee
                                // with this name who does NOT already have any SS Code assigned.
                                // This handles the very first import where no SS Codes exist yet.
                                if (emp == null)
                                {
                                    string normName = NormalizeName(fullName);
                                    var nameMatches = existingEmployees.Where(e =>
                                        NormalizeName(e.FullName).Equals(normName, StringComparison.OrdinalIgnoreCase) &&
                                        string.IsNullOrEmpty(e.SsCode)).ToList();

                                    // Only use the name match if it is unambiguous (exactly one hit)
                                    if (nameMatches.Count == 1)
                                        emp = nameMatches[0];
                                    // If 0 or 2+ matches → treat as new employee (will be created below)
                                }
                            }
                            else
                            {
                                // No SS Code in the Excel row at all — fall back to name only
                                string normName = NormalizeName(fullName);
                                emp = existingEmployees.FirstOrDefault(e =>
                                    NormalizeName(e.FullName).Equals(normName, StringComparison.OrdinalIgnoreCase));
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
                                var newEmp = new Employee
                                {
                                    FirstName = names[0],
                                    LastName = names.Length > 1 ? names[1] : "",
                                    FatherName = fatherName,
                                    Designation = desig,
                                    Department = section,
                                    SsCode = ssCode,
                                    UanNumber = uan,
                                    EsiNumber = esi,
                                    BankAccountNo = bankAcc,
                                    IfscCode = ifsc,
                                    BaseSalary = salary,
                                    StaffType = GuessStaffType(desig),
                                    JoiningDate = DateTime.Now,
                                    IsActive = true,
                                    Photo = Array.Empty<byte>()
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

        // ── Helpers ──

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // Remove titles like Mr. Mrs. Ms. Dr. and extra spaces
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
    }
}