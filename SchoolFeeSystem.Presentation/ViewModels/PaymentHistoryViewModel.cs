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
using System.Windows.Documents;
using System.Windows.Threading;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class PaymentHistoryViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PaymentLogService _paymentLogService;

        // Guard flag: prevents OnSelectedPaymentTypeChanged from firing
        // during construction before the DataGrid is ready.
        private bool _isInitializing = true;

        // ── Filters ───────────────────────────────────────────────────────────
        // Initialize to string.Empty so WPF binding never triggers a null→""
        // change notification during DataGrid initialization.
        [ObservableProperty] private string studentIdFilter = string.Empty;
        [ObservableProperty] private string studentNameFilter = string.Empty;
        [ObservableProperty] private DateTime? startDate;
        [ObservableProperty] private DateTime? endDate;

        public ObservableCollection<string> PaymentTypes { get; } = new()
        { "All", "Fee", "Fine Waiver", "Fine", "Other" };

        [ObservableProperty] private string selectedPaymentType = "All";

        // ── Grid data ─────────────────────────────────────────────────────────
        // Manually implemented so we can ensure PropertyChanged is only ever
        // raised on the UI thread at DispatcherPriority.Loaded — this prevents
        // the DataGrid from receiving a new ItemsSource before its internal
        // ItemCollection is ready, which would throw:
        //   "Items collection must be empty before using ItemsSource."
        private DataView _paymentHistoryView;
        public DataView PaymentHistoryView
        {
            get => _paymentHistoryView;
            private set
            {
                if (_paymentHistoryView == value) return;
                _paymentHistoryView = value;
                OnPropertyChanged(nameof(PaymentHistoryView));
            }
        }

        private DataView _financialSummaryView;
        public DataView FinancialSummaryView
        {
            get => _financialSummaryView;
            private set
            {
                if (_financialSummaryView == value) return;
                _financialSummaryView = value;
                OnPropertyChanged(nameof(FinancialSummaryView));
            }
        }

        // ── Row selection → receipt preview ──────────────────────────────────
        [ObservableProperty] private DataRowView selectedPaymentRow;

        // Receipt panel fields (bound in XAML receipt preview border)
        [ObservableProperty] private string receiptNumber = "";
        [ObservableProperty] private string receiptDate = "";
        [ObservableProperty] private string receiptStudentName = "";
        [ObservableProperty] private string receiptStudentId = "";
        [ObservableProperty] private string receiptGuardian = "";
        [ObservableProperty] private string receiptPaymentMode = "";
        [ObservableProperty] private string receiptAmount = "";
        [ObservableProperty] private string receiptQuarter = "";
        [ObservableProperty] private string receiptRemarks = "";

        [ObservableProperty] private Visibility receiptPanelVisibility = Visibility.Collapsed;
        [ObservableProperty] private Visibility financialSummaryEmptyVisibility = Visibility.Visible;

        // ── Summary cards ─────────────────────────────────────────────────────
        [ObservableProperty] private string totalPaidAmount = "₹0.00";
        [ObservableProperty] private string pendingAmount = "—";
        [ObservableProperty] private string totalFineAmount = "₹0.00";

        [ObservableProperty] private bool isExporting;

        // ─────────────────────────────────────────────────────────────────────
        public PaymentHistoryViewModel(CsvDataService csvService,
                                       PaymentLogService paymentLogService)
        {
            _csvService = csvService;
            _paymentLogService = paymentLogService;
            // Do NOT load data here. The View calls Initialize() from its
            // Loaded event, guaranteeing the DataGrid is fully ready first.
        }

        /// <summary>
        /// Called by PaymentHistoryView.Loaded — at this point the DataGrid's
        /// ItemCollection is empty and ready to accept an ItemsSource.
        /// </summary>
        public void Initialize()
        {
            _isInitializing = false;
            LoadAllPayments();
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS — all DataView assignments go through the Dispatcher so the
        // DataGrid's ItemCollection is fully ready before the swap occurs.
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Assigns a new DataView to PaymentHistoryView. The property setter
        /// automatically dispatches the PropertyChanged notification at
        /// DispatcherPriority.Loaded so the DataGrid is always ready first.
        /// </summary>
        private void SetPaymentHistoryView(DataView newView, DataTable summarySource = null)
        {
            PaymentHistoryView = null;
            PaymentHistoryView = newView;
            if (summarySource != null)
                UpdateSummary(summarySource);
        }

        /// <summary>
        /// Assigns a new DataView to FinancialSummaryView. Same dispatch
        /// guarantee as SetPaymentHistoryView.
        /// </summary>
        private void SetFinancialSummaryView(DataView newView, bool isEmpty)
        {
            FinancialSummaryView = null;
            FinancialSummaryView = newView;
            FinancialSummaryEmptyVisibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═════════════════════════════════════════════════════════════════════
        // LOAD / FILTER
        // ═════════════════════════════════════════════════════════════════════

        private void LoadAllPayments()
        {
            var all = _paymentLogService.GetPaymentHistory();
            var newView = new DataView(all);
            SetPaymentHistoryView(newView, all);
        }

        /// <summary>
        /// Filters on the dedicated "Student Name" and "Student ID" columns
        /// that PaymentLogService now writes for every transaction.
        /// Both filters support partial / case-insensitive matching.
        /// </summary>
        [RelayCommand]
        public void SearchByStudent()
        {
            bool hasName = !string.IsNullOrWhiteSpace(StudentNameFilter);
            bool hasId = !string.IsNullOrWhiteSpace(StudentIdFilter);

            if (!hasName && !hasId) { LoadAllPayments(); return; }

            var all = _paymentLogService.GetPaymentHistory();
            var filtered = all.Clone();

            foreach (DataRow row in all.Rows)
            {
                bool ok = true;

                if (hasName)
                    ok = row["Student Name"].ToString()
                             .Contains(StudentNameFilter, StringComparison.OrdinalIgnoreCase);

                if (ok && hasId)
                    ok = row["Student ID"].ToString()
                             .Contains(StudentIdFilter, StringComparison.OrdinalIgnoreCase);

                if (ok) filtered.ImportRow(row);
            }

            SetPaymentHistoryView(new DataView(filtered), filtered);

            // Financial summary uses the same search key
            string key = hasName ? StudentNameFilter : StudentIdFilter;
            var summary = _paymentLogService.GetStudentFinancialSummary(key);
            bool isEmpty = (summary == null || summary.Rows.Count == 0);
            var summaryDv = isEmpty ? null : new DataView(summary);
            SetFinancialSummaryView(summaryDv, isEmpty);
        }

        [RelayCommand]
        public void FilterByDateRange()
        {
            if (!StartDate.HasValue && !EndDate.HasValue) { LoadAllPayments(); return; }

            var all = _paymentLogService.GetPaymentHistory();
            var filtered = all.Clone();

            foreach (DataRow row in all.Rows)
            {
                if (!DateTime.TryParse(row["Payment Date"].ToString(), out DateTime pd)) continue;
                bool ok = true;
                if (StartDate.HasValue && pd.Date < StartDate.Value.Date) ok = false;
                if (EndDate.HasValue && pd.Date > EndDate.Value.Date) ok = false;
                if (ok) filtered.ImportRow(row);
            }

            SetPaymentHistoryView(new DataView(filtered), filtered);
        }

        partial void OnSelectedPaymentTypeChanged(string value)
        {
            // Skip during constructor — DataGrid is not ready yet
            if (_isInitializing) return;

            if (value == "All") { LoadAllPayments(); return; }

            var all = _paymentLogService.GetPaymentHistory();
            var filtered = all.Clone();

            foreach (DataRow row in all.Rows)
            {
                string mode = row["Payment Mode"].ToString();
                bool match = value switch
                {
                    "Fee" => mode != "Fine Waiver",
                    "Fine Waiver" => mode == "Fine Waiver",
                    _ => true
                };
                if (match) filtered.ImportRow(row);
            }

            SetPaymentHistoryView(new DataView(filtered), filtered);
        }

        // These partial methods intentionally do nothing.
        // Filtering is driven explicitly by the Search button only.
        // Without these, the source generator fires a change callback when
        // WPF first binds the TextBoxes (null → ""), which crashes the
        // DataGrid with "Items collection must be empty before using ItemsSource".
        partial void OnStudentIdFilterChanged(string value) { }
        partial void OnStudentNameFilterChanged(string value) { }
        partial void OnStartDateChanged(DateTime? value) { }
        partial void OnEndDateChanged(DateTime? value) { }

        [RelayCommand]
        public void ClearFilters()
        {
            StudentIdFilter = string.Empty;
            StudentNameFilter = string.Empty;
            StartDate = null;
            EndDate = null;

            // Temporarily suppress the type-change handler to avoid a
            // redundant reload — LoadAllPayments() below covers it.
            _isInitializing = true;
            SelectedPaymentType = "All";
            _isInitializing = false;

            SetFinancialSummaryView(null, true);
            ReceiptPanelVisibility = Visibility.Collapsed;
            LoadAllPayments();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ROW SELECTION → RECEIPT PREVIEW
        // ═════════════════════════════════════════════════════════════════════

        partial void OnSelectedPaymentRowChanged(DataRowView value)
        {
            if (value == null) { ReceiptPanelVisibility = Visibility.Collapsed; return; }
            PopulateReceiptFields(value.Row);
        }

        // Per-row 🖨️ button calls this with CommandParameter="{Binding}"
        // which is the DataRowView for that exact row.
        [RelayCommand]
        public void PrintReceiptForRow(object parameter)
        {
            if (parameter is DataRowView drv)
            {
                PopulateReceiptFields(drv.Row);
                SelectedPaymentRow = drv;
                DoPrint();
            }
        }

        private void PopulateReceiptFields(DataRow row)
        {
            ReceiptNumber = row["Payment ID"]?.ToString() ?? "";
            ReceiptDate = row["Payment Date"]?.ToString() ?? "";
            ReceiptStudentName = row["Student Name"]?.ToString() ?? "";
            ReceiptStudentId = row["Student ID"]?.ToString() ?? "";
            ReceiptGuardian = row["Guardian"]?.ToString() ?? "";
            ReceiptPaymentMode = row["Payment Mode"]?.ToString() ?? "";
            ReceiptQuarter = row["Quarter"]?.ToString() ?? "";
            ReceiptRemarks = row["Remarks"]?.ToString() ?? "";

            string rawAmt = row["Amount"]?.ToString() ?? "";
            ReceiptAmount = decimal.TryParse(rawAmt, out decimal amt) ? $"₹{amt:N2}" : rawAmt;

            ReceiptPanelVisibility = Visibility.Visible;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PRINT RECEIPT
        // ═════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void PrintReceipt()
        {
            if (SelectedPaymentRow == null)
            {
                MessageBox.Show(
                    "Click the 🖨️ Receipt button on a row, or select a row first.",
                    "No Row Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DoPrint();
        }

        private void DoPrint()
        {
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() != true) return;

            var doc = BuildReceiptDocument(pd.PrintableAreaWidth, pd.PrintableAreaHeight);
            IDocumentPaginatorSource src = doc;
            pd.PrintDocument(src.DocumentPaginator,
                $"Receipt – {ReceiptStudentName} – {ReceiptDate}");
        }

        private System.Windows.Documents.FlowDocument BuildReceiptDocument(
            double pageWidth, double pageHeight)
        {
            var doc = new System.Windows.Documents.FlowDocument
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                PageHeight = pageHeight,
                PageWidth = pageWidth,
                PagePadding = new Thickness(48),
                ColumnWidth = pageWidth
            };

            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run("SCHOOL FEE MANAGEMENT SYSTEM"))
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });

            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run("Official Fee Payment Receipt"))
            {
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            });

            doc.Blocks.Add(HRule());

            var tbl = new System.Windows.Documents.Table();
            tbl.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(160) });
            tbl.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            tbl.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(160) });
            tbl.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var rg = new System.Windows.Documents.TableRowGroup();
            tbl.RowGroups.Add(rg);

            void AddRow(string l1, string v1, string l2 = "", string v2 = "")
            {
                var tr = new System.Windows.Documents.TableRow();
                tr.Cells.Add(LabelCell(l1)); tr.Cells.Add(ValueCell(v1));
                tr.Cells.Add(LabelCell(l2)); tr.Cells.Add(ValueCell(v2));
                rg.Rows.Add(tr);
            }

            AddRow("Receipt No:", ReceiptNumber, "Date:", ReceiptDate);
            AddRow("Student Name:", ReceiptStudentName, "Student ID:", ReceiptStudentId);
            AddRow("Guardian:", ReceiptGuardian, "Quarter:", ReceiptQuarter);
            AddRow("Mode:", ReceiptPaymentMode, "Amount Paid:", ReceiptAmount);
            doc.Blocks.Add(tbl);

            if (!string.IsNullOrWhiteSpace(ReceiptRemarks))
                doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                    new System.Windows.Documents.Run($"Remarks: {ReceiptRemarks}"))
                {
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 10, 0, 0)
                });

            doc.Blocks.Add(HRule());

            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run($"AMOUNT PAID:  {ReceiptAmount}"))
            {
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.DarkGreen,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 16)
            });

            doc.Blocks.Add(HRule());

            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(
                    $"Computer-generated receipt. Printed: {DateTime.Now:dd-MM-yyyy HH:mm}"))
            {
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextAlignment = TextAlignment.Center
            });

            return doc;
        }

        private static System.Windows.Documents.BlockUIContainer HRule()
        {
            var r = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = System.Windows.Media.Brushes.LightGray,
                Margin = new Thickness(0, 6, 0, 6)
            };
            return new System.Windows.Documents.BlockUIContainer(r);
        }

        private static System.Windows.Documents.TableCell LabelCell(string t) =>
            new(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(t))
            {
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 4, 8, 4)
            });

        private static System.Windows.Documents.TableCell ValueCell(string t) =>
            new(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(t ?? ""))
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 16, 4)
            });

        // ═════════════════════════════════════════════════════════════════════
        // SUMMARY
        // ═════════════════════════════════════════════════════════════════════

        private void UpdateSummary(DataTable payments)
        {
            decimal totalPaid = 0m, totalFines = 0m;
            foreach (DataRow row in payments.Rows)
            {
                if (!decimal.TryParse(row["Amount"]?.ToString(), out decimal amt)) continue;
                totalPaid += amt;
                if (row["Payment Mode"]?.ToString() == "Fine Waiver")
                    totalFines += amt;
            }
            TotalPaidAmount = $"₹{totalPaid:N2}";
            TotalFineAmount = $"₹{totalFines:N2}";
            PendingAmount = "—";
        }

        // ═════════════════════════════════════════════════════════════════════
        // EXPORT
        // ═════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void ExportToExcel()
        {
            try
            {
                IsExporting = true;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"PaymentHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                var wb = new ClosedXML.Excel.XLWorkbook();

                void FillSheet(string name, DataView dv, string hex)
                {
                    if (dv == null || dv.Count == 0) return;
                    var ws = wb.AddWorksheet(name);
                    var t = dv.ToTable();
                    for (int i = 0; i < t.Columns.Count; i++)
                    {
                        var cell = ws.Cell(1, i + 1);
                        cell.Value = t.Columns[i].ColumnName;
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml(hex);
                        cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    }
                    for (int r = 0; r < t.Rows.Count; r++)
                        for (int c = 0; c < t.Columns.Count; c++)
                            ws.Cell(r + 2, c + 1).Value = t.Rows[r][c]?.ToString();
                    ws.Columns().AdjustToContents();
                }

                FillSheet("Payment History", PaymentHistoryView, "#2196F3");
                FillSheet("Financial Summary", FinancialSummaryView, "#4CAF50");

                wb.SaveAs(dlg.FileName);
                MessageBox.Show($"✅ Exported!\n\n{dlg.FileName}",
                    "Exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Export failed:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsExporting = false; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // REFRESH / BACK
        // ═════════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void RefreshData()
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(StudentNameFilter)
                          || !string.IsNullOrWhiteSpace(StudentIdFilter);
            if (hasFilter) SearchByStudent();
            else LoadAllPayments();
            MessageBox.Show("Payment history refreshed!",
                "Refreshed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public void GoBack() =>
            Application.Current.MainWindow.Content =
                App.Current.Services.GetRequiredService<DashboardView>();
    }
}