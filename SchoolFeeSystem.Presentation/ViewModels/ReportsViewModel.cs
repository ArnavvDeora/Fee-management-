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
using Microsoft.Win32;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PdfReportService _pdfService;
        private readonly PaymentLogService _paymentLogService;

        private DataTable _originalData;
        private string _currentSheetName;

        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        // Report Type Options - UPDATED with Payment Logs
        public ObservableCollection<string> ReportTypes { get; } = new()
        {
            "Individual Student Report",
            "Pending Fees Summary",
            "All Students Summary",
            "Custom Filter Report",
            "Payment Transaction Logs"  // NEW: View all payment logs
        };

        [ObservableProperty]
        private string selectedReportType = "Individual Student Report";

        [ObservableProperty]
        private string selectedSheet;

        [ObservableProperty]
        private string sheetSearchText;

        [ObservableProperty]
        private DataView currentSheetView;

        [ObservableProperty]
        private string searchText;

        [ObservableProperty]
        private DataRowView selectedRow;

        // Statistics
        [ObservableProperty]
        private int totalStudents;

        [ObservableProperty]
        private int studentsWithPending;

        [ObservableProperty]
        private string totalPendingAmount;

        // Payment Log Statistics
        [ObservableProperty]
        private int totalTransactions;

        [ObservableProperty]
        private string totalPaymentsCollected;

        [ObservableProperty]
        private DateTime paymentLogStartDate = DateTime.Now.AddMonths(-1);

        [ObservableProperty]
        private DateTime paymentLogEndDate = DateTime.Now;

        public ReportsViewModel(CsvDataService csvService, PdfReportService pdfService, PaymentLogService paymentLogService)
        {
            _csvService = csvService;
            _pdfService = pdfService;
            _paymentLogService = paymentLogService;

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }
        }

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();

            if (string.IsNullOrWhiteSpace(value))
            {
                foreach (var name in SheetNames)
                    FilteredSheetNames.Add(name);
            }
            else
            {
                foreach (var name in SheetNames)
                {
                    if (name.ToLower().Contains(value.ToLower()))
                        FilteredSheetNames.Add(name);
                }
            }
        }

        partial void OnSelectedSheetChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            SearchText = string.Empty;
            _currentSheetName = _csvService.GetSheetNameFromDisplay(value);

            var table = _csvService.GetSheet(_currentSheetName);
            if (table != null)
            {
                _originalData = table;
                CurrentSheetView = _originalData.DefaultView;
                UpdateStatistics();
            }
        }

        partial void OnSelectedReportTypeChanged(string value)
        {
            // If user selects payment logs, load them
            if (value == "Payment Transaction Logs")
            {
                LoadPaymentLogs();
            }
        }

        private void UpdateStatistics()
        {
            if (CurrentSheetView == null) return;

            var table = CurrentSheetView.Table;
            TotalStudents = table.Rows.Count;

            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") ||
                                     c.ColumnName.ToLower().Contains("previous"));

            if (pendingCol != null)
            {
                decimal totalPending = 0;
                int countPending = 0;

                foreach (DataRow row in table.Rows)
                {
                    string raw = row[pendingCol]?.ToString()?.Trim();
                    if (decimal.TryParse(raw, out decimal pending) && pending > 0)
                    {
                        totalPending += pending;
                        countPending++;
                    }
                }

                StudentsWithPending = countPending;
                TotalPendingAmount = $"₹{totalPending:N2}";
            }
            else
            {
                StudentsWithPending = 0;
                TotalPendingAmount = "N/A";
            }
        }

        // NEW: Load payment transaction logs
        private void LoadPaymentLogs()
        {
            var logs = _paymentLogService.GetLogsByDateRange(PaymentLogStartDate, PaymentLogEndDate);
            var logsTable = _paymentLogService.GetLogsAsDataTable(logs);

            _originalData = logsTable;
            CurrentSheetView = logsTable.DefaultView;

            // Update stats
            TotalTransactions = logs.Count;
            TotalPaymentsCollected = $"₹{logs.Sum(l => l.AmountPaid):N2}";
        }

        [RelayCommand]
        public void SearchStudent()
        {
            if (_originalData == null) return;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                CurrentSheetView = _originalData.DefaultView;
                CurrentSheetView.RowFilter = string.Empty;
                UpdateStatistics();
                return;
            }

            try
            {
                var table = _originalData;
                var conditions = table.Columns
                    .Cast<DataColumn>()
                    .Select(c => $"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{SearchText.Replace("'", "''")}%'");

                var filterString = string.Join(" OR ", conditions);
                CurrentSheetView.RowFilter = filterString;
            }
            catch
            {
                CurrentSheetView.RowFilter = string.Empty;
                MessageBox.Show(
                    "Search filter could not be applied. Please try a different search term.",
                    "Filter Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            if (CurrentSheetView != null)
            {
                CurrentSheetView.RowFilter = string.Empty;
                UpdateStatistics();
            }
        }

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
                MessageBox.Show("Please select a sheet first.", "No Sheet Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var metadata = _csvService.GetSheetMetadata(_currentSheetName);

            switch (SelectedReportType)
            {
                case "Individual Student Report":
                    GenerateIndividualReport(metadata);
                    break;

                case "Pending Fees Summary":
                    GeneratePendingFeesReport(metadata);
                    break;

                case "All Students Summary":
                    GenerateAllStudentsReport(metadata);
                    break;

                case "Custom Filter Report":
                    GenerateCustomFilterReport(metadata);
                    break;

                default:
                    GenerateIndividualReport(metadata);
                    break;
            }
        }

        // NEW: Generate payment logs report
        private void GeneratePaymentLogsReport()
        {
            var logs = _paymentLogService.GetLogsByDateRange(PaymentLogStartDate, PaymentLogEndDate);

            if (logs.Count == 0)
            {
                MessageBox.Show(
                    $"No payment transactions found between {PaymentLogStartDate:dd-MM-yyyy} and {PaymentLogEndDate:dd-MM-yyyy}.",
                    "No Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
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
                        // Export as CSV
                        _paymentLogService.ExportToCsv(dlg.FileName, logs);
                        MessageBox.Show(
                            $"✅ Payment Logs CSV Generated Successfully!\n\n" +
                            $"Total Transactions: {logs.Count}\n" +
                            $"Total Amount: ₹{logs.Sum(l => l.AmountPaid):N2}\n" +
                            $"Date Range: {PaymentLogStartDate:dd-MM-yyyy} to {PaymentLogEndDate:dd-MM-yyyy}",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        // Export as PDF
                        var logsTable = _paymentLogService.GetLogsAsDataTable(logs);
                        _pdfService.GenerateSummaryReport(
                            logsTable,
                            dlg.FileName,
                            $"Payment Transaction Logs ({PaymentLogStartDate:dd-MM-yyyy} to {PaymentLogEndDate:dd-MM-yyyy})",
                            null
                        );

                        MessageBox.Show(
                            $"✅ Payment Logs Report Generated Successfully!\n\n" +
                            $"Total Transactions: {logs.Count}\n" +
                            $"Total Amount: ₹{logs.Sum(l => l.AmountPaid):N2}\n" +
                            $"Date Range: {PaymentLogStartDate:dd-MM-yyyy} to {PaymentLogEndDate:dd-MM-yyyy}",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    if (MessageBox.Show("Would you like to open the report?", "Open Report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateIndividualReport(CsvDataService.SheetMetadata metadata)
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("Please select a student row first.", "No Student Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Student_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateStudentReport(SelectedRow.Row, dlg.FileName, metadata);
                    MessageBox.Show("Individual Student Report Generated Successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the report?", "Open Report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GeneratePendingFeesReport(CsvDataService.SheetMetadata metadata)
        {
            var pendingView = _csvService.GetPendingFeesView(_currentSheetName);

            if (pendingView == null || pendingView.Count == 0)
            {
                MessageBox.Show("No pending fees found for this sheet.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Pending_Fees_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(pendingView.Table, dlg.FileName, "Pending Fees Report", metadata);
                    MessageBox.Show($"Pending Fees Report Generated Successfully!\n\nTotal Students with Pending Fees: {pendingView.Count}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the report?", "Open Report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateAllStudentsReport(CsvDataService.SheetMetadata metadata)
        {
            if (_originalData == null || _originalData.Rows.Count == 0)
            {
                MessageBox.Show("No data available in the selected sheet.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"All_Students_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(_originalData, dlg.FileName, "All Students Fee Report", metadata);
                    MessageBox.Show($"All Students Report Generated Successfully!\n\nTotal Students: {_originalData.Rows.Count}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the report?", "Open Report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateCustomFilterReport(CsvDataService.SheetMetadata metadata)
        {
            if (CurrentSheetView == null || CurrentSheetView.Count == 0)
            {
                MessageBox.Show("No data available. Please apply a search filter first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dataToExport = CurrentSheetView.ToTable();

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Custom_Filter_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _pdfService.GenerateSummaryReport(dataToExport, dlg.FileName, "Custom Filtered Report", metadata);
                    MessageBox.Show($"Custom Report Generated Successfully!\n\nTotal Records: {dataToExport.Rows.Count}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the report?", "Open Report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dlg.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            SearchText = string.Empty;
            if (CurrentSheetView != null)
                CurrentSheetView.RowFilter = string.Empty;

            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}