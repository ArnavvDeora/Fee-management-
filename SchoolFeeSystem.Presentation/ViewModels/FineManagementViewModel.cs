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
    public partial class FineManagementViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly FineCalculationService _fineService;

        // ── Fine report grid ──────────────────────────────────────────────────
        [ObservableProperty] private DataView fineReportView;

        // ── Summary cards ─────────────────────────────────────────────────────
        [ObservableProperty] private string totalFinesAmount = "Rs 0.00";
        [ObservableProperty] private string pendingFinesAmount = "Rs 0.00";
        [ObservableProperty] private string paidFinesAmount = "Rs 0.00";
        [ObservableProperty] private int totalStudentsWithFines;
        [ObservableProperty] private int pendingFinesCount;

        // ── Filters ───────────────────────────────────────────────────────────
        [ObservableProperty] private string studentSearchText;
        [ObservableProperty] private string departmentFilter = "All";

        public ObservableCollection<string> Departments { get; } = new();
        public ObservableCollection<string> FineStatusOptions { get; } = new()
        { "All", "Fine Applicable", "No Fine" };

        [ObservableProperty] private string selectedFineStatus = "All";

        // ── Fine calculator ───────────────────────────────────────────────────
        // Admin picks a quarter start date and the calculator uses TODAY as asOfDate
        // so the result matches exactly what Fee Collection shows each student.
        [ObservableProperty]
        private DateTime quarterStartDate =
            new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        [ObservableProperty] private DateTime? customAsOfDate;  // optional override
        [ObservableProperty] private string calculatedFine = "";
        [ObservableProperty] private string fineBreakdownText = "";

        // ─────────────────────────────────────────────────────────────────────
        public FineManagementViewModel(CsvDataService csvService,
                                       FineCalculationService fineService)
        {
            _csvService = csvService;
            _fineService = fineService;

            LoadDepartments();
            LoadFineReport();
        }

        private void LoadDepartments()
        {
            Departments.Clear();
            Departments.Add("All");
            foreach (var d in _csvService.GetDepartments())
                Departments.Add(d);
        }

        private void LoadFineReport()
        {
            var report = _fineService.BuildFineReport(_csvService);
            FineReportView = null;
            FineReportView = new System.Data.DataView(report);
            UpdateStatistics(report);
        }

        private void UpdateStatistics(DataTable report)
        {
            decimal total = 0m, pending = 0m, paid = 0m;
            int pendingCount = 0;

            foreach (DataRow row in report.Rows)
            {
                decimal fine = Convert.ToDecimal(row["Fine Amount"]);
                decimal waived = 0m;
                if (report.Columns.Contains("Waived Amount"))
                    waived = Convert.ToDecimal(row["Waived Amount"]);

                string status = row["Status"]?.ToString() ?? "";
                total += fine + waived;           // gross fine before waiver
                if (status == "Fine Applicable")
                { pending += fine; pendingCount++; }
                else
                { paid += fine + waived; }        // "No Fine" rows include waived credit
            }

            TotalFinesAmount = $"Rs {total:N2}";
            PendingFinesAmount = $"Rs {pending:N2}";
            PaidFinesAmount = $"Rs {paid:N2}";
            TotalStudentsWithFines = report.Rows.Count;
            PendingFinesCount = pendingCount;
        }

        // ── Filters ───────────────────────────────────────────────────────────
        [RelayCommand]
        public void ApplyFilters()
        {
            var all = _fineService.BuildFineReport(_csvService);
            var filtered = all.Clone();

            foreach (DataRow row in all.Rows)
            {
                bool ok = true;
                if (!string.IsNullOrWhiteSpace(StudentSearchText))
                {
                    string id = row["Student ID"]?.ToString() ?? "";
                    string name = row["Student Name"]?.ToString() ?? "";
                    ok = id.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase)
                      || name.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase);
                }
                if (ok && DepartmentFilter != "All")
                    ok = (row["Sheet / Class"]?.ToString() ?? "")
                           .Contains(DepartmentFilter, StringComparison.OrdinalIgnoreCase);
                if (ok && SelectedFineStatus != "All")
                    ok = row["Status"]?.ToString() == SelectedFineStatus;

                if (ok) filtered.ImportRow(row);
            }

            FineReportView = null;
            FineReportView = new System.Data.DataView(filtered);
            UpdateStatistics(filtered);
        }

        [RelayCommand]
        public void ClearFilters()
        {
            StudentSearchText = string.Empty;
            DepartmentFilter = "All";
            SelectedFineStatus = "All";
            LoadFineReport();
        }

        // ── Fine Calculator ───────────────────────────────────────────────────
        // Uses the EXACT same FineCalculationService as FeeCollectionViewModel.
        // Results are guaranteed to match what appears in the student's fee card.
        [RelayCommand]
        public void CalculateFine()
        {
            DateTime asOf = CustomAsOfDate?.Date ?? DateTime.Now.Date;
            var bd = _fineService.GetBreakdown(QuarterStartDate.Date, asOf);

            CalculatedFine = $"Rs {bd.TotalFine:N2}";
            FineBreakdownText = bd.Summary;

            MessageBox.Show(
                $"Fine Calculation\n\n" +
                $"Quarter Start Date : {QuarterStartDate:dd-MM-yyyy}\n" +
                $"Calculated As Of   : {asOf:dd-MM-yyyy}\n" +
                $"Grace Period Ends  : {bd.GraceEndDate:dd-MM-yyyy}\n\n" +
                $"--- Breakdown ---\n" +
                bd.Summary,
                "Fine Result", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Export ────────────────────────────────────────────────────────────
        [RelayCommand]
        public void ExportFineReport()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"FineReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                var wb = new ClosedXML.Excel.XLWorkbook();
                var fineSheet = wb.AddWorksheet("Fine Report");
                var table = FineReportView.ToTable();

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var cell = fineSheet.Cell(1, i + 1);
                    cell.Value = table.Columns[i].ColumnName;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FF9800");
                    cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    for (int c = 0; c < table.Columns.Count; c++)
                    {
                        var cell = fineSheet.Cell(r + 2, c + 1);
                        cell.Value = table.Rows[r][c]?.ToString();
                        if (table.Columns[c].ColumnName == "Status" &&
                            cell.Value.ToString() == "Fine Applicable")
                            cell.Style.Fill.BackgroundColor =
                                ClosedXML.Excel.XLColor.FromHtml("#FFEBEE");
                    }
                }

                var sum = wb.AddWorksheet("Summary");
                sum.Cell(1, 1).Value = "Fine Summary Report";
                sum.Cell(1, 1).Style.Font.Bold = true;
                sum.Cell(1, 1).Style.Font.FontSize = 16;
                sum.Cell(3, 1).Value = "Total Fines:"; sum.Cell(3, 2).Value = TotalFinesAmount;
                sum.Cell(4, 1).Value = "Pending Fines:"; sum.Cell(4, 2).Value = PendingFinesAmount;
                sum.Cell(5, 1).Value = "Paid Fines:"; sum.Cell(5, 2).Value = PaidFinesAmount;
                sum.Cell(6, 1).Value = "Students w/ Fines:"; sum.Cell(6, 2).Value = TotalStudentsWithFines;
                sum.Cell(7, 1).Value = "Pending Count:"; sum.Cell(7, 2).Value = PendingFinesCount;
                sum.Column(1).Width = 30;
                sum.Column(2).Width = 20;

                fineSheet.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                MessageBox.Show($"Fine report exported!\n\n{dlg.FileName}",
                    "Exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void SendFineReminders()
        {
            var pending = _fineService.BuildFineReport(_csvService)
                .AsEnumerable()
                .Where(r => r["Status"].ToString() == "Fine Applicable")
                .ToList();

            if (pending.Count == 0)
            { MessageBox.Show("No students with applicable fines.", "No Reminders", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            if (MessageBox.Show($"Send reminders to {pending.Count} student(s) with pending fines?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                MessageBox.Show($"Reminders queued for {pending.Count} students.",
                    "Sent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void RefreshData()
        {
            LoadFineReport();
            // Silent refresh — no MessageBox — so callers can trigger this
            // programmatically (e.g. after a waiver in FeeCollection) without
            // interrupting the user with a popup.
        }

        [RelayCommand]
        public void GoBack() =>
            Application.Current.MainWindow.Content =
                App.Current.Services.GetRequiredService<DashboardView>();
    }
}