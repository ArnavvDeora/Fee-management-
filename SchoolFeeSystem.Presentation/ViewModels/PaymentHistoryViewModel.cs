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

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class PaymentHistoryViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PaymentLogService _paymentLogService;

        // Guard: prevents filter callbacks from firing before the DataGrid is ready.
        private bool _isInitializing = true;

        // ── Filters ───────────────────────────────────────────────────────────
        [ObservableProperty] private string studentIdFilter = string.Empty;
        [ObservableProperty] private string studentNameFilter = string.Empty;
        [ObservableProperty] private DateTime? startDate;
        [ObservableProperty] private DateTime? endDate;

        public ObservableCollection<string> PaymentTypes { get; } = new()
        { "All", "Fee", "Fine Waiver", "Fine", "Other" };

        [ObservableProperty] private string selectedPaymentType = "All";

        // ── Grid data — manually notified so DataGrid is always ready first ──
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

        // Receipt fields
        [ObservableProperty] private string receiptNumber = "";
        [ObservableProperty] private string receiptDate = "";
        [ObservableProperty] private string receiptStudentName = "";
        [ObservableProperty] private string receiptStudentId = "";
        [ObservableProperty] private string receiptGuardian = "";
        [ObservableProperty] private string receiptPaymentMode = "";
        [ObservableProperty] private string receiptAmount = "";
        [ObservableProperty] private string receiptQuarter = "";
        [ObservableProperty] private string receiptCourse = "";
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
        }

        /// <summary>
        /// Called by PaymentHistoryView.Loaded after the DataGrid is fully ready.
        /// </summary>
        public void Initialize()
        {
            _isInitializing = false;
            LoadAllPayments();
        }

        // ═════════════════════════════════════════════════════════════════════
        // INTERNAL HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private void SetPaymentHistoryView(DataView newView, DataTable summarySource = null)
        {
            PaymentHistoryView = null;
            PaymentHistoryView = newView;
            if (summarySource != null)
                UpdateSummaryCards(summarySource);
        }

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
            SetPaymentHistoryView(new DataView(all), all);
        }

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

            string key = hasName ? StudentNameFilter : StudentIdFilter;
            var summary = _paymentLogService.GetStudentFinancialSummary(key);
            bool isEmpty = summary == null || summary.Rows.Count == 0;
            SetFinancialSummaryView(isEmpty ? null : new DataView(summary), isEmpty);
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

            ReceiptCourse = row.Table.Columns.Contains("Course")
                ? row["Course"]?.ToString() ?? ""
                : "";

            string rawAmt = row["Amount"]?.ToString() ?? "";
            ReceiptAmount = decimal.TryParse(rawAmt, out decimal amt)
                ? $"₹{amt:N2}"
                : rawAmt;

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

            // PrintVisual avoids the FlowDocument paginator entirely.
            // The paginator is what causes text to render one character
            // per line when ColumnWidth = PositiveInfinity.
            var visual = BuildReceiptVisual(pd.PrintableAreaWidth, pd.PrintableAreaHeight);
            pd.PrintVisual(visual, $"Receipt – {ReceiptStudentName} – {ReceiptDate}");
        }

        private System.Windows.Media.Visual BuildReceiptVisual(
            double pageWidth, double pageHeight)
        {
            // ── Brushes ──────────────────────────────────────────────────────
            var black = System.Windows.Media.Brushes.Black;
            var nearBlack = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(30, 30, 30));
            var midGrey = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(120, 120, 120));
            var lightGrey = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(220, 220, 220));
            var white = System.Windows.Media.Brushes.White;

            // ── Fonts ────────────────────────────────────────────────────────
            var sans = new System.Windows.Media.FontFamily("Segoe UI");
            var serif = new System.Windows.Media.FontFamily("Georgia");
            var mono = new System.Windows.Media.FontFamily("Courier New");

            const double marginH = 60;
            double contentW = pageWidth - marginH * 2;

            // ── Root page border ─────────────────────────────────────────────
            var root = new System.Windows.Controls.Border
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = white,
                Padding = new Thickness(marginH, 48, marginH, 48)
            };

            var stack = new System.Windows.Controls.StackPanel
            {
                Width = contentW,
                Orientation = System.Windows.Controls.Orientation.Vertical
            };
            root.Child = stack;

            // ── Local helpers ────────────────────────────────────────────────

            System.Windows.Controls.TextBlock Txt(
                string text,
                double size = 11,
                FontWeight? weight = null,
                System.Windows.Media.Brush fg = null,
                System.Windows.Media.FontFamily font = null,
                TextAlignment align = TextAlignment.Left,
                Thickness? margin = null,
                bool wrap = false)
            {
                return new System.Windows.Controls.TextBlock
                {
                    Text = text ?? "",
                    FontSize = size,
                    FontWeight = weight ?? FontWeights.Normal,
                    Foreground = fg ?? nearBlack,
                    FontFamily = font ?? sans,
                    TextAlignment = align,
                    TextWrapping = wrap
                        ? System.Windows.TextWrapping.Wrap
                        : System.Windows.TextWrapping.NoWrap,
                    Margin = margin ?? new Thickness(0)
                };
            }

            void HR(double h = 1, System.Windows.Media.Brush br = null,
                    double top = 0, double bot = 0)
            {
                stack.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = h,
                    Fill = br ?? lightGrey,
                    Margin = new Thickness(0, top, 0, bot)
                });
            }

            void Section(string label)
            {
                stack.Children.Add(Txt(label, 8, FontWeights.Bold, midGrey,
                                       margin: new Thickness(0, 16, 0, 2)));
                HR(0.5, lightGrey, 0, 8);
            }

            // Two-column row: fixed-width label, remaining width for value
            void Field2(string l1, string v1, string l2, string v2,
                        double labelW = 120)
            {
                double half = contentW / 2;
                var outer = new System.Windows.Controls.Grid
                { Margin = new Thickness(0, 3, 0, 3) };
                outer.ColumnDefinitions.Add(
                    new System.Windows.Controls.ColumnDefinition
                    { Width = new GridLength(half) });
                outer.ColumnDefinitions.Add(
                    new System.Windows.Controls.ColumnDefinition
                    { Width = new GridLength(half) });

                System.Windows.Controls.Grid Pair(string lbl, string val)
                {
                    var pg = new System.Windows.Controls.Grid();
                    pg.ColumnDefinitions.Add(
                        new System.Windows.Controls.ColumnDefinition
                        { Width = new GridLength(labelW) });
                    pg.ColumnDefinitions.Add(
                        new System.Windows.Controls.ColumnDefinition
                        { Width = new GridLength(half - labelW) });

                    var lt = Txt(lbl, 9, null, midGrey);
                    System.Windows.Controls.Grid.SetColumn(lt, 0);

                    var vt = Txt(val ?? "—", 11, FontWeights.SemiBold, wrap: true);
                    System.Windows.Controls.Grid.SetColumn(vt, 1);

                    pg.Children.Add(lt);
                    pg.Children.Add(vt);
                    return pg;
                }

                var left = Pair(l1, v1);
                var right = Pair(l2, v2);
                System.Windows.Controls.Grid.SetColumn(left, 0);
                System.Windows.Controls.Grid.SetColumn(right, 1);
                outer.Children.Add(left);
                outer.Children.Add(right);
                stack.Children.Add(outer);
            }

            // ── INSTITUTE HEADER ─────────────────────────────────────────────
            stack.Children.Add(Txt(
                "CENTRAL INSTITUTE OF HAND TOOLS, JALANDHAR",
                17, FontWeights.Bold, black, serif,
                TextAlignment.Center, new Thickness(0, 0, 0, 3)));

            stack.Children.Add(Txt(
                "Official Fee Payment Receipt",
                10, null, midGrey, null,
                TextAlignment.Center, new Thickness(0, 0, 0, 14)));

            HR(1.5, black);

            // Receipt No | Date (two fixed-width halves — no star columns)
            {
                double half = contentW / 2;
                var g = new System.Windows.Controls.Grid
                { Margin = new Thickness(0, 8, 0, 8) };
                g.ColumnDefinitions.Add(
                    new System.Windows.Controls.ColumnDefinition
                    { Width = new GridLength(half) });
                g.ColumnDefinitions.Add(
                    new System.Windows.Controls.ColumnDefinition
                    { Width = new GridLength(half) });

                var rn = Txt($"Receipt No: {ReceiptNumber}", 8, null, midGrey, mono);
                System.Windows.Controls.Grid.SetColumn(rn, 0);

                var dt = Txt($"Date: {ReceiptDate}", 8, null, midGrey, mono,
                             TextAlignment.Right);
                System.Windows.Controls.Grid.SetColumn(dt, 1);

                g.Children.Add(rn);
                g.Children.Add(dt);
                stack.Children.Add(g);
            }

            HR(0.5, lightGrey);

            // ── STUDENT DETAILS ───────────────────────────────────────────────
            Section("STUDENT DETAILS");
            Field2("Student Name", ReceiptStudentName, "Student ID", ReceiptStudentId);
            Field2("Guardian", ReceiptGuardian, "Course / Dept", ReceiptCourse);

            // ── PAYMENT DETAILS ───────────────────────────────────────────────
            Section("PAYMENT DETAILS");
            Field2("Payment Mode", ReceiptPaymentMode, "Quarter / Period", ReceiptQuarter);

            // ── REMARKS ───────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(ReceiptRemarks))
            {
                Section("REMARKS");
                stack.Children.Add(Txt(ReceiptRemarks, 10, null, midGrey,
                                       wrap: true,
                                       margin: new Thickness(0, 0, 0, 4)));
            }

            HR(0.5, lightGrey, 16, 0);

            // ── AMOUNT BOX ────────────────────────────────────────────────────
            var amtBorder = new System.Windows.Controls.Border
            {
                Background = nearBlack,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(24, 16, 24, 16),
                Margin = new Thickness(0, 16, 0, 16)
            };
            var amtPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            amtPanel.Children.Add(Txt("AMOUNT PAID  ", 11, FontWeights.SemiBold, lightGrey));
            amtPanel.Children.Add(Txt(ReceiptAmount, 28, FontWeights.Bold, white, serif));
            amtBorder.Child = amtPanel;
            stack.Children.Add(amtBorder);

            HR(1.5, black);

            // ── FOOTER ────────────────────────────────────────────────────────
            stack.Children.Add(Txt(
                $"Computer-generated receipt. No signature required." +
                $"          Printed: {DateTime.Now:dd-MM-yyyy HH:mm}",
                8, null, midGrey, mono,
                TextAlignment.Center, new Thickness(0, 10, 0, 0)));

            // Force WPF to measure and arrange before the print driver reads pixels
            root.Measure(new Size(pageWidth, pageHeight));
            root.Arrange(new Rect(0, 0, pageWidth, pageHeight));
            root.UpdateLayout();

            return root;
        }

        // ═════════════════════════════════════════════════════════════════════
        // SUMMARY CARDS
        // ═════════════════════════════════════════════════════════════════════

        private void UpdateSummaryCards(DataTable payments)
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
        // EXPORT TO EXCEL
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

                FillSheet("Payment History", PaymentHistoryView, "#1565C0");
                FillSheet("Financial Summary", FinancialSummaryView, "#2E7D32");

                wb.SaveAs(dlg.FileName);
                MessageBox.Show($"✅ Exported successfully!\n\n{dlg.FileName}",
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
        }

        [RelayCommand]
        public void GoBack() =>
            Application.Current.MainWindow.Content =
                App.Current.Services.GetRequiredService<DashboardView>();
    }
}