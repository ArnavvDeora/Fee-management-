using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Wordprocessing;
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
    public partial class PaymentHistoryViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;

        // Student filter
        [ObservableProperty]
        private string studentIdFilter;

        [ObservableProperty]
        private string studentNameFilter;

        // Payment history data
        [ObservableProperty]
        private DataView paymentHistoryView;

        // Financial summary
        [ObservableProperty]
        private DataView financialSummaryView;

        [ObservableProperty]
        private string totalPaidAmount;

        [ObservableProperty]
        private string pendingAmount;

        [ObservableProperty]
        private string totalFineAmount;

        // Date filter
        [ObservableProperty]
        private DateTime? startDate;

        [ObservableProperty]
        private DateTime? endDate;

        // Payment type filter
        public ObservableCollection<string> PaymentTypes { get; } = new()
        {
            "All",
            "Fee",
            "Fine",
            "Other"
        };

        [ObservableProperty]
        private string selectedPaymentType = "All";

        // Export functionality
        [ObservableProperty]
        private bool isExporting;

        public PaymentHistoryViewModel(CsvDataService csvService)
        {
            _csvService = csvService;
            LoadAllPayments();
        }

        private void LoadAllPayments()
        {
            var allPayments = _csvService.GetPaymentHistory();
            PaymentHistoryView = allPayments.DefaultView;
            UpdateSummary(allPayments);
        }

        [RelayCommand]
        public void SearchByStudent()
        {
            if (string.IsNullOrWhiteSpace(StudentIdFilter) &&
                string.IsNullOrWhiteSpace(StudentNameFilter))
            {
                LoadAllPayments();
                return;
            }

            var allPayments = _csvService.GetPaymentHistory();
            var filtered = allPayments.Clone();

            foreach (DataRow row in allPayments.Rows)
            {
                bool matches = true;

                if (!string.IsNullOrWhiteSpace(StudentIdFilter))
                {
                    matches = row["Student ID"].ToString()
                        .Contains(StudentIdFilter, StringComparison.OrdinalIgnoreCase);
                }

                if (matches && !string.IsNullOrWhiteSpace(StudentNameFilter))
                {
                    // You might need to join with student data to filter by name
                    // For now, this is a placeholder
                    matches = true;
                }

                if (matches)
                    filtered.ImportRow(row);
            }

            PaymentHistoryView = filtered.DefaultView;
            UpdateSummary(filtered);

            // Load financial summary for specific student
            if (!string.IsNullOrWhiteSpace(StudentIdFilter))
            {
                var summary = _csvService.GetStudentFinancialSummary(StudentIdFilter);
                FinancialSummaryView = summary.DefaultView;
            }
        }

        [RelayCommand]
        public void FilterByDateRange()
        {
            if (!StartDate.HasValue && !EndDate.HasValue)
            {
                LoadAllPayments();
                return;
            }

            var allPayments = _csvService.GetPaymentHistory();
            var filtered = allPayments.Clone();

            foreach (DataRow row in allPayments.Rows)
            {
                DateTime paymentDate = DateTime.Parse(row["Payment Date"].ToString());
                bool inRange = true;

                if (StartDate.HasValue && paymentDate < StartDate.Value)
                    inRange = false;

                if (EndDate.HasValue && paymentDate > EndDate.Value)
                    inRange = false;

                if (inRange)
                    filtered.ImportRow(row);
            }

            PaymentHistoryView = filtered.DefaultView;
            UpdateSummary(filtered);
        }

        partial void OnSelectedPaymentTypeChanged(string value)
        {
            if (value == "All")
            {
                LoadAllPayments();
                return;
            }

            var allPayments = _csvService.GetPaymentHistory();
            var filtered = allPayments.Clone();

            foreach (DataRow row in allPayments.Rows)
            {
                if (row["Payment Type"].ToString() == value)
                    filtered.ImportRow(row);
            }

            PaymentHistoryView = filtered.DefaultView;
            UpdateSummary(filtered);
        }

        [RelayCommand]
        public void ClearFilters()
        {
            StudentIdFilter = string.Empty;
            StudentNameFilter = string.Empty;
            StartDate = null;
            EndDate = null;
            SelectedPaymentType = "All";
            FinancialSummaryView = null;
            LoadAllPayments();
        }

        private void UpdateSummary(DataTable payments)
        {
            decimal totalPaid = 0m;
            decimal totalFines = 0m;

            foreach (DataRow row in payments.Rows)
            {
                decimal amount = decimal.Parse(row["Amount"].ToString());
                string type = row["Payment Type"].ToString();

                totalPaid += amount;

                if (type == "Fine")
                    totalFines += amount;
            }

            TotalPaidAmount = $"₹{totalPaid:N2}";
            TotalFineAmount = $"₹{totalFines:N2}";
        }

        [RelayCommand]
        public void ExportToExcel()
        {
            try
            {
                IsExporting = true;

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"PaymentHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var workbook = new ClosedXML.Excel.XLWorkbook();

                    // Export payment history
                    if (PaymentHistoryView != null && PaymentHistoryView.Count > 0)
                    {
                        var paymentSheet = workbook.AddWorksheet("Payment History");
                        var paymentTable = PaymentHistoryView.ToTable();

                        // Add headers
                        for (int i = 0; i < paymentTable.Columns.Count; i++)
                        {
                            paymentSheet.Cell(1, i + 1).Value = paymentTable.Columns[i].ColumnName;
                            paymentSheet.Cell(1, i + 1).Style.Font.Bold = true;
                            paymentSheet.Cell(1, i + 1).Style.Fill.BackgroundColor =
                                ClosedXML.Excel.XLColor.LightGray;
                        }

                        // Add data
                        for (int row = 0; row < paymentTable.Rows.Count; row++)
                        {
                            for (int col = 0; col < paymentTable.Columns.Count; col++)
                            {
                                paymentSheet.Cell(row + 2, col + 1).Value =
                                    paymentTable.Rows[row][col]?.ToString();
                            }
                        }

                        paymentSheet.Columns().AdjustToContents();
                    }

                    // Export financial summary if available
                    if (FinancialSummaryView != null && FinancialSummaryView.Count > 0)
                    {
                        var summarySheet = workbook.AddWorksheet("Financial Summary");
                        var summaryTable = FinancialSummaryView.ToTable();

                        for (int i = 0; i < summaryTable.Columns.Count; i++)
                        {
                            summarySheet.Cell(1, i + 1).Value = summaryTable.Columns[i].ColumnName;
                            summarySheet.Cell(1, i + 1).Style.Font.Bold = true;
                            summarySheet.Cell(1, i + 1).Style.Fill.BackgroundColor =
                                ClosedXML.Excel.XLColor.LightBlue;
                        }

                        for (int row = 0; row < summaryTable.Rows.Count; row++)
                        {
                            for (int col = 0; col < summaryTable.Columns.Count; col++)
                            {
                                summarySheet.Cell(row + 2, col + 1).Value =
                                    summaryTable.Rows[row][col]?.ToString();
                            }
                        }

                        summarySheet.Columns().AdjustToContents();
                    }

                    workbook.SaveAs(saveDialog.FileName);

                    MessageBox.Show(
                        $"✅ Payment history exported successfully!\n\nFile saved to:\n{saveDialog.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to export payment history:\n\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
            }
        }

        [RelayCommand]
        public void RefreshData()
        {
            if (!string.IsNullOrWhiteSpace(StudentIdFilter))
            {
                SearchByStudent();
            }
            else
            {
                LoadAllPayments();
            }

            MessageBox.Show(
                "Payment history refreshed!",
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