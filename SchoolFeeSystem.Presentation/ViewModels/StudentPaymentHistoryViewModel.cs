using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StudentPaymentHistoryViewModel : ObservableObject
    {
        private readonly PaymentLogService _paymentLogService;
        private readonly PdfReportService _pdfService;
        private readonly CsvDataService _csvService;

        public ObservableCollection<string> StudentNames { get; } = new();
        public ObservableCollection<string> FilteredStudentNames { get; } = new();

        [ObservableProperty]
        private string selectedStudent;

        [ObservableProperty]
        private string studentSearchText;

        [ObservableProperty]
        private DataView paymentHistory;

        [ObservableProperty]
        private int totalTransactions;

        [ObservableProperty]
        private decimal totalAmountPaid;

        [ObservableProperty]
        private decimal currentBalance;

        [ObservableProperty]
        private string studentPhoneNumber;

        [ObservableProperty]
        private DateTime startDate = DateTime.Now.AddMonths(-6);

        [ObservableProperty]
        private DateTime endDate = DateTime.Now;

        // Selected transaction for receipt generation
        [ObservableProperty]
        private DataRowView selectedTransaction;

        public StudentPaymentHistoryViewModel(
            PaymentLogService paymentLogService,
            PdfReportService pdfService,
            CsvDataService csvService)
        {
            _paymentLogService = paymentLogService;
            _pdfService = pdfService;
            _csvService = csvService;

            LoadStudentNames();
        }

        private void LoadStudentNames()
        {
            StudentNames.Clear();
            FilteredStudentNames.Clear();

            // Get unique student names from all sheets
            var allStudents = _csvService.GetSheetNames()
                .SelectMany(sheet =>
                {
                    var table = _csvService.GetSheet(sheet);
                    if (table == null) return Enumerable.Empty<string>();

                    var nameCol = table.Columns.Cast<DataColumn>()
                        .FirstOrDefault(c => c.ColumnName.ToLower().Contains("name") &&
                                           !c.ColumnName.ToLower().Contains("father"));

                    if (nameCol == null) return Enumerable.Empty<string>();

                    return table.Rows.Cast<DataRow>()
                        .Select(r => r[nameCol]?.ToString()?.Trim())
                        .Where(n => !string.IsNullOrWhiteSpace(n));
                })
                .Distinct()
                .OrderBy(n => n);

            foreach (var name in allStudents)
            {
                StudentNames.Add(name);
                FilteredStudentNames.Add(name);
            }
        }

        partial void OnStudentSearchTextChanged(string value)
        {
            FilteredStudentNames.Clear();

            if (string.IsNullOrWhiteSpace(value))
            {
                foreach (var name in StudentNames)
                    FilteredStudentNames.Add(name);
            }
            else
            {
                foreach (var name in StudentNames)
                {
                    if (name.ToLower().Contains(value.ToLower()))
                        FilteredStudentNames.Add(name);
                }
            }
        }

        partial void OnSelectedStudentChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                LoadPaymentHistory(value);
            }
        }

        partial void OnStartDateChanged(DateTime value)
        {
            if (!string.IsNullOrEmpty(SelectedStudent))
            {
                LoadPaymentHistory(SelectedStudent);
            }
        }

        partial void OnEndDateChanged(DateTime value)
        {
            if (!string.IsNullOrEmpty(SelectedStudent))
            {
                LoadPaymentHistory(SelectedStudent);
            }
        }

        private void LoadPaymentHistory(string studentName)
        {
            // Get payment logs for this student
            var logs = _paymentLogService.GetLogsForStudent(studentName)
                .Where(l => l.PaymentDate >= StartDate && l.PaymentDate <= EndDate)
                .OrderByDescending(l => l.PaymentDate)
                .ToList();

            // Convert to DataTable for DataGrid
            var table = new DataTable();
            table.Columns.Add("Date", typeof(string));
            table.Columns.Add("Amount Paid", typeof(string));
            table.Columns.Add("Payment Mode", typeof(string));
            table.Columns.Add("Previous Balance", typeof(string));
            table.Columns.Add("New Balance", typeof(string));
            table.Columns.Add("Course", typeof(string));
            table.Columns.Add("Period", typeof(string));
            table.Columns.Add("Transaction ID", typeof(string));
            table.Columns.Add("Processed By", typeof(string));

            foreach (var log in logs)
            {
                table.Rows.Add(
                    log.PaymentDate.ToString("dd-MM-yyyy HH:mm"),
                    $"₹{log.AmountPaid:N2}",
                    log.PaymentMode,
                    $"₹{log.PreviousBalance:N2}",
                    $"₹{log.NewBalance:N2}",
                    log.CourseName,
                    log.Period,
                    log.TransactionId,
                    log.ProcessedBy
                );
            }

            PaymentHistory = table.DefaultView;

            // Calculate statistics
            TotalTransactions = logs.Count;
            TotalAmountPaid = logs.Sum(l => l.AmountPaid);
            CurrentBalance = logs.FirstOrDefault()?.NewBalance ?? 0;
            StudentPhoneNumber = logs.FirstOrDefault()?.PhoneNumber ?? "";
        }

        [RelayCommand]
        public void GenerateReceipt()
        {
            if (SelectedTransaction == null)
            {
                MessageBox.Show(
                    "Please select a transaction to generate receipt.",
                    "No Transaction Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Payment_Receipt_{SelectedStudent}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    GeneratePaymentReceipt(SelectedTransaction, dlg.FileName);

                    MessageBox.Show(
                        "Payment receipt generated successfully!",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the receipt?", "Open Receipt",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
                    MessageBox.Show(
                        $"Failed to generate receipt: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void PrintReceipt()
        {
            if (SelectedTransaction == null)
            {
                MessageBox.Show(
                    "Please select a transaction to print receipt.",
                    "No Transaction Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Generate temp PDF
                string tempFile = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"Receipt_{DateTime.Now:yyyyMMddHHmmss}.pdf");

                GeneratePaymentReceipt(SelectedTransaction, tempFile);

                // Print the PDF
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true,
                    Verb = "print"
                };
                System.Diagnostics.Process.Start(psi);

                MessageBox.Show(
                    "Receipt sent to printer!",
                    "Print",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to print receipt: {ex.Message}",
                    "Print Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void GeneratePaymentReceipt(DataRowView transaction, string filePath)
        {
            // Extract transaction details
            string date = transaction["Date"]?.ToString() ?? "";
            string amount = transaction["Amount Paid"]?.ToString() ?? "";
            string mode = transaction["Payment Mode"]?.ToString() ?? "";
            string prevBalance = transaction["Previous Balance"]?.ToString() ?? "";
            string newBalance = transaction["New Balance"]?.ToString() ?? "";
            string course = transaction["Course"]?.ToString() ?? "";
            string period = transaction["Period"]?.ToString() ?? "";
            string txnId = transaction["Transaction ID"]?.ToString() ?? "";

            // Create receipt using PdfReportService
            // (You'll need to add this method to PdfReportService)
            _pdfService.GeneratePaymentReceipt(
                studentName: SelectedStudent,
                phoneNumber: StudentPhoneNumber,
                transactionId: txnId,
                paymentDate: date,
                amountPaid: amount,
                paymentMode: mode,
                previousBalance: prevBalance,
                newBalance: newBalance,
                course: course,
                period: period,
                filePath: filePath
            );
        }

        [RelayCommand]
        public void ExportHistory()
        {
            if (PaymentHistory == null || PaymentHistory.Count == 0)
            {
                MessageBox.Show(
                    "No payment history to export.",
                    "No Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|CSV Files (*.csv)|*.csv",
                FileName = $"Payment_History_{SelectedStudent}_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    if (dlg.FileName.EndsWith(".csv"))
                    {
                        ExportToCsv(dlg.FileName);
                    }
                    else
                    {
                        ExportToPdf(dlg.FileName);
                    }

                    MessageBox.Show(
                        "Payment history exported successfully!",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    if (MessageBox.Show("Would you like to open the file?", "Open File",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
                    MessageBox.Show(
                        $"Failed to export: {ex.Message}",
                        "Export Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            using (var writer = new System.IO.StreamWriter(filePath))
            {
                // Write header
                writer.WriteLine($"Payment History for {SelectedStudent}");
                writer.WriteLine($"Phone: {StudentPhoneNumber}");
                writer.WriteLine($"Period: {StartDate:dd-MM-yyyy} to {EndDate:dd-MM-yyyy}");
                writer.WriteLine($"Total Transactions: {TotalTransactions}");
                writer.WriteLine($"Total Amount Paid: ₹{TotalAmountPaid:N2}");
                writer.WriteLine($"Current Balance: ₹{CurrentBalance:N2}");
                writer.WriteLine();

                // Write column headers
                writer.WriteLine("Date,Amount Paid,Payment Mode,Previous Balance,New Balance,Course,Period,Transaction ID,Processed By");

                // Write data
                foreach (DataRowView row in PaymentHistory)
                {
                    writer.WriteLine($"\"{row["Date"]}\",\"{row["Amount Paid"]}\",\"{row["Payment Mode"]}\",\"{row["Previous Balance"]}\",\"{row["New Balance"]}\",\"{row["Course"]}\",\"{row["Period"]}\",\"{row["Transaction ID"]}\",\"{row["Processed By"]}\"");
                }
            }
        }

        private void ExportToPdf(string filePath)
        {
            var table = PaymentHistory.ToTable();
            _pdfService.GenerateSummaryReport(
                table,
                filePath,
                $"Payment History - {SelectedStudent}",
                null
            );
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}