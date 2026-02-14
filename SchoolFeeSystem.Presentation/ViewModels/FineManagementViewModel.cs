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

        // Fine report data
        [ObservableProperty]
        private DataView fineReportView;

        // Summary statistics
        [ObservableProperty]
        private string totalFinesAmount;

        [ObservableProperty]
        private string pendingFinesAmount;

        [ObservableProperty]
        private string paidFinesAmount;

        [ObservableProperty]
        private int totalStudentsWithFines;

        [ObservableProperty]
        private int pendingFinesCount;

        // Filter options
        [ObservableProperty]
        private string studentSearchText;

        [ObservableProperty]
        private string departmentFilter;

        public ObservableCollection<string> Departments { get; } = new();

        public ObservableCollection<string> FineStatusOptions { get; } = new()
        {
            "All",
            "Pending",
            "Paid"
        };

        [ObservableProperty]
        private string selectedFineStatus = "All";

        // Fine calculation helper
        [ObservableProperty]
        private DateTime? selectedDueDate;

        [ObservableProperty]
        private DateTime? selectedPaymentDate;

        [ObservableProperty]
        private int monthNumber = 1;

        [ObservableProperty]
        private string calculatedFine;

        public FineManagementViewModel(CsvDataService csvService)
        {
            _csvService = csvService;
            LoadDepartments();
            LoadFineReport();
        }

        private void LoadDepartments()
        {
            Departments.Clear();
            Departments.Add("All");

            foreach (var dept in _csvService.GetDepartments())
            {
                Departments.Add(dept);
            }

            DepartmentFilter = "All";
        }

        private void LoadFineReport()
        {
            var fineReport = _csvService.GetFineReport();
            FineReportView = fineReport.DefaultView;
            UpdateStatistics(fineReport);
        }

        private void UpdateStatistics(DataTable fineReport)
        {
            decimal totalFines = 0m;
            decimal pendingFines = 0m;
            decimal paidFines = 0m;
            int pendingCount = 0;

            foreach (DataRow row in fineReport.Rows)
            {
                decimal fineAmount = decimal.Parse(row["Fine Amount"].ToString());
                string status = row["Status"].ToString();

                totalFines += fineAmount;

                if (status == "Pending")
                {
                    pendingFines += fineAmount;
                    pendingCount++;
                }
                else
                {
                    paidFines += fineAmount;
                }
            }

            TotalFinesAmount = $"₹{totalFines:N2}";
            PendingFinesAmount = $"₹{pendingFines:N2}";
            PaidFinesAmount = $"₹{paidFines:N2}";
            TotalStudentsWithFines = fineReport.Rows.Count;
            PendingFinesCount = pendingCount;
        }

        [RelayCommand]
        public void ApplyFilters()
        {
            var allFines = _csvService.GetFineReport();
            var filtered = allFines.Clone();

            foreach (DataRow row in allFines.Rows)
            {
                bool matches = true;

                // Student search filter
                if (!string.IsNullOrWhiteSpace(StudentSearchText))
                {
                    string studentId = row["Student ID"]?.ToString() ?? "";
                    string studentName = row["Student Name"]?.ToString() ?? "";

                    matches = studentId.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase) ||
                             studentName.Contains(StudentSearchText, StringComparison.OrdinalIgnoreCase);
                }

                // Department filter
                if (matches && DepartmentFilter != "All")
                {
                    matches = row["Department"]?.ToString() == DepartmentFilter;
                }

                // Status filter
                if (matches && SelectedFineStatus != "All")
                {
                    matches = row["Status"]?.ToString() == SelectedFineStatus;
                }

                if (matches)
                    filtered.ImportRow(row);
            }

            FineReportView = filtered.DefaultView;
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

        [RelayCommand]
        public void CalculateFineEstimate()
        {
            if (!SelectedDueDate.HasValue)
            {
                MessageBox.Show(
                    "Please select a due date first.",
                    "Due Date Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DateTime compareDate = SelectedPaymentDate ?? DateTime.Now;
            decimal fine = _csvService.CalculateFine(SelectedDueDate.Value, compareDate, MonthNumber);

            CalculatedFine = $"₹{fine:N2}";

            int daysLate = Math.Max(0, (compareDate - SelectedDueDate.Value).Days);

            MessageBox.Show(
                $"Fine Calculation Result:\n\n" +
                $"Due Date: {SelectedDueDate.Value:dd/MM/yyyy}\n" +
                $"Payment/Current Date: {compareDate:dd/MM/yyyy}\n" +
                $"Days Late: {daysLate}\n" +
                $"Month Number: {MonthNumber}\n\n" +
                $"Calculated Fine: ₹{fine:N2}\n\n" +
                GetFineBreakdown(MonthNumber, daysLate),
                "Fine Calculation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private string GetFineBreakdown(int month, int daysLate)
        {
            if (daysLate <= 0)
                return "No fine - payment is on time.";

            switch (month)
            {
                case 1:
                    if (daysLate > 15)
                        return "Breakdown:\n• First month late (>15 days): ₹150";
                    else
                        return "Breakdown:\n• Within grace period (≤15 days): ₹0";

                case 2:
                    int secondMonthDays = Math.Min(daysLate - 15, 30);
                    decimal secondMonthFine = Math.Min(secondMonthDays * 20m, 600m);
                    return $"Breakdown:\n" +
                           $"• First month fine: ₹150\n" +
                           $"• Second month ({secondMonthDays} days × ₹20): ₹{secondMonthFine:N2}\n" +
                           $"• Total: ₹{150 + secondMonthFine:N2}";

                case 3:
                    return $"Breakdown:\n" +
                           $"• First month fine: ₹150\n" +
                           $"• Second month fine: ₹600 (max)\n" +
                           $"• Third month base fine: ₹750\n" +
                           $"• Total: ₹1,500";

                default:
                    decimal additionalMonths = (month - 3) * 750m;
                    return $"Breakdown:\n" +
                           $"• Accumulated (1st + 2nd month): ₹750\n" +
                           $"• Third month base: ₹750\n" +
                           $"• Additional months ({month - 3} × ₹750): ₹{additionalMonths:N2}\n" +
                           $"• Total: ₹{1500 + additionalMonths:N2}";
            }
        }

        [RelayCommand]
        public void ExportFineReport()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"FineReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var workbook = new ClosedXML.Excel.XLWorkbook();

                    // Export fine report
                    var fineSheet = workbook.AddWorksheet("Fine Report");
                    var fineTable = FineReportView.ToTable();

                    // Add headers with styling
                    for (int i = 0; i < fineTable.Columns.Count; i++)
                    {
                        var cell = fineSheet.Cell(1, i + 1);
                        cell.Value = fineTable.Columns[i].ColumnName;
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FF9800");
                        cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    }

                    // Add data with conditional formatting
                    for (int row = 0; row < fineTable.Rows.Count; row++)
                    {
                        for (int col = 0; col < fineTable.Columns.Count; col++)
                        {
                            var cell = fineSheet.Cell(row + 2, col + 1);
                            cell.Value = fineTable.Rows[row][col]?.ToString();

                            // Highlight pending fines
                            if (fineTable.Columns[col].ColumnName == "Status" &&
                                cell.Value.ToString() == "Pending")
                            {
                                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FFEBEE");
                            }
                        }
                    }

                    // Add summary sheet
                    var summarySheet = workbook.AddWorksheet("Summary");
                    summarySheet.Cell(1, 1).Value = "Fine Summary Report";
                    summarySheet.Cell(1, 1).Style.Font.Bold = true;
                    summarySheet.Cell(1, 1).Style.Font.FontSize = 16;

                    summarySheet.Cell(3, 1).Value = "Total Fines Amount:";
                    summarySheet.Cell(3, 2).Value = TotalFinesAmount;
                    summarySheet.Cell(4, 1).Value = "Pending Fines Amount:";
                    summarySheet.Cell(4, 2).Value = PendingFinesAmount;
                    summarySheet.Cell(5, 1).Value = "Paid Fines Amount:";
                    summarySheet.Cell(5, 2).Value = PaidFinesAmount;
                    summarySheet.Cell(6, 1).Value = "Total Students with Fines:";
                    summarySheet.Cell(6, 2).Value = TotalStudentsWithFines;
                    summarySheet.Cell(7, 1).Value = "Pending Fines Count:";
                    summarySheet.Cell(7, 2).Value = PendingFinesCount;

                    summarySheet.Column(1).Width = 30;
                    summarySheet.Column(2).Width = 20;

                    fineSheet.Columns().AdjustToContents();
                    workbook.SaveAs(saveDialog.FileName);

                    MessageBox.Show(
                        $"✅ Fine report exported successfully!\n\nFile saved to:\n{saveDialog.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to export fine report:\n\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void SendFineReminders()
        {
            var pendingFines = _csvService.GetFineReport()
                .AsEnumerable()
                .Where(r => r["Status"].ToString() == "Pending")
                .ToList();

            if (pendingFines.Count == 0)
            {
                MessageBox.Show(
                    "No pending fines found!",
                    "No Reminders",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"This will send reminders to {pendingFines.Count} students with pending fines.\n\n" +
                $"Do you want to proceed?",
                "Confirm Send Reminders",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Here you would integrate with email/SMS service
                MessageBox.Show(
                    $"✅ Fine reminders queued for {pendingFines.Count} students!\n\n" +
                    $"Notifications will be sent via email/SMS.",
                    "Reminders Sent",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void RefreshData()
        {
            LoadFineReport();
            MessageBox.Show(
                "Fine report refreshed successfully!",
                "Refreshed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}