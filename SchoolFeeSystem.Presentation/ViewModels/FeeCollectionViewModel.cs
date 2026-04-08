using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ════════════════════════════════════════════════════════════════════════
    // FeeCollectionViewModel
    // ════════════════════════════════════════════════════════════════════════
    public partial class FeeCollectionViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PaymentLogService _paymentLogService;
        private readonly AcademicCycleService _cycleService;
        private readonly FineCalculationService _fineService;

        private DataTable _fullSheetData;
        private string _currentSheetName;
        private DateTime _currentQuarterStart;

        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();
        public ObservableCollection<string> PaymentModes { get; } = new()
        {
            "Cash", "UPI", "Net Banking", "Credit Card", "Debit Card", "Cheque"
        };
        public ObservableCollection<string> FeeFilterOptions { get; } = new()
        {
            "All Students", "Pending Fees Only", "No Pending Fees"
        };

        // ── Sheet / filter ────────────────────────────────────────────────
        [ObservableProperty] private string selectedSheet;
        [ObservableProperty] private string sheetSearchText;
        [ObservableProperty] private DataView pendingFeesView;
        [ObservableProperty] private DataRowView selectedRow;
        [ObservableProperty] private string selectedFeeFilter = "All Students";

        // ── Card list + selection ─────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<FeeStudentCard> feeStudentCards = new();
        [ObservableProperty] private FeeStudentCard selectedStudentCard;

        // ── Summary stat chips ────────────────────────────────────────────
        [ObservableProperty] private int summaryTotalStudents;
        [ObservableProperty] private int summaryPaidStudents;
        [ObservableProperty] private int summaryPendingStudents;
        [ObservableProperty] private decimal summaryPendingAmount;

        // ── Payment ───────────────────────────────────────────────────────
        [ObservableProperty] private string paymentAmount = "0";
        private decimal PaymentAmountDecimal =>
            decimal.TryParse(PaymentAmount, out decimal v) ? v : 0m;

        [ObservableProperty] private string selectedPaymentMode = "Cash";

        // ── Selected student info (right-side payment panel) ──────────────
        [ObservableProperty] private string studentName;
        [ObservableProperty] private string studentPhoneNumber;
        [ObservableProperty] private string studentGuardianName;
        [ObservableProperty] private string studentId;
        [ObservableProperty] private string currentQuarter;

        [ObservableProperty] private decimal previousPendingAmount;

        // Quarterly fee BEFORE scholarship  (shown as "Quarterly Fees" label)
        [ObservableProperty] private decimal quarterlyFeeRawAmount;

        // Scholarship % applied this quarter (0 if none)
        [ObservableProperty] private decimal scholarshipPercentage;

        // The rupee discount  =  quarterlyFeeRawAmount × scholarshipPercentage / 100
        [ObservableProperty] private decimal scholarshipDiscountAmount;

        // Quarterly fee AFTER scholarship  (used in TotalDue calculation)
        [ObservableProperty] private decimal quarterlyFeeAmount;

        [ObservableProperty] private decimal currentFineAmount;

        [ObservableProperty] private string fineWaiverAmount = "0";
        private decimal FineWaiverAmountDecimal =>
            decimal.TryParse(FineWaiverAmount, out decimal v) ? v : 0m;

        [ObservableProperty] private decimal netFineAfterWaiver;
        [ObservableProperty] private decimal totalPendingForSelectedStudent;
        [ObservableProperty] private string fineBreakdownText;

        // ── Note / increment bar ──────────────────────────────────────────
        [ObservableProperty] private string noteInformation;
        [ObservableProperty] private DateTime extensionDate = DateTime.Now.AddMonths(1);
        [ObservableProperty] private bool hasActiveNote;

        // ═════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ═════════════════════════════════════════════════════════════════

        public FeeCollectionViewModel(CsvDataService csvService,
                                      PaymentLogService paymentLogService,
                                      AcademicCycleService cycleService,
                                      FineCalculationService fineService)
        {
            _csvService = csvService;
            _paymentLogService = paymentLogService;
            _cycleService = cycleService;
            _fineService = fineService;

            var transitions = _cycleService.RunCycleCheck();
            if (transitions.Count > 0)
            {
                string msg = string.Join("\n", transitions.Select(t =>
                    $"• {t.OldSheet} -> {t.NewQuarter} ({t.StudentsCarried} students)"));
                MessageBox.Show(
                    $"Quarter Transition Completed!\n\n{msg}\n\n" +
                    "Fee data has been reset for the new quarter.\n" +
                    "Unpaid balances have been carried forward.\n" +
                    "⚠️ Scholarships have been cleared — please reapply for the new quarter.",
                    "Academic Cycle Update", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            foreach (var displayName in _csvService.GetSheetDisplayNames())
            {
                if (displayName.Contains("_PaymentHistory") ||
                    displayName.ToLower().Contains("payment history"))
                    continue;
                SheetNames.Add(displayName);
                FilteredSheetNames.Add(displayName);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // PROPERTY CHANGE HANDLERS
        // ═════════════════════════════════════════════════════════════════

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();
            var src = string.IsNullOrWhiteSpace(value)
                ? SheetNames
                : (IEnumerable<string>)SheetNames.Where(n =>
                    n.ToLower().Contains(value.ToLower()));
            foreach (var n in src) FilteredSheetNames.Add(n);
        }

        partial void OnSelectedSheetChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                LoadSheetData(value);
                UpdateNoteInformation();
            }
        }

        partial void OnSelectedFeeFilterChanged(string value) => ApplyFeeFilter();

        partial void OnFineWaiverAmountChanged(string value)
        {
            decimal v = FineWaiverAmountDecimal;
            if (v < 0) { FineWaiverAmount = "0"; return; }
            if (v > CurrentFineAmount) { FineWaiverAmount = CurrentFineAmount.ToString("F2"); return; }
            NetFineAfterWaiver = CurrentFineAmount - v;
            TotalPendingForSelectedStudent =
                PreviousPendingAmount + QuarterlyFeeAmount + NetFineAfterWaiver;
        }

        partial void OnSelectedRowChanged(DataRowView value)
        {
            if (value != null) UpdateSelectedStudentInfo();
            else ClearStudentInfo();
        }

        // ═════════════════════════════════════════════════════════════════
        // DATA LOADING
        // ═════════════════════════════════════════════════════════════════

        private void LoadSheetData(string displayName)
        {
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);

            if (_fullSheetData != null)
            {
                var meta = _csvService.GetSheetMetadata(_currentSheetName);
                _currentQuarterStart = DetermineQuarterStart(_fullSheetData, meta?.Period);
                _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);
            }

            ApplyFeeFilter();
        }

        // ═════════════════════════════════════════════════════════════════
        // FILTER
        // ═════════════════════════════════════════════════════════════════

        private void ApplyFeeFilter()
        {
            if (_fullSheetData == null) return;

            var table = _fullSheetData;
            var prevCol = FindCol(table, "previous", "pending");
            var quarterlyCol = FindCol(table, "quarterly fees", "installment");
            var fineCol = FindFineCol(table);

            if ((prevCol == null && quarterlyCol == null) || SelectedFeeFilter == "All Students")
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PendingFeesView = null;
                    PendingFeesView = new System.Data.DataView(table);
                }, System.Windows.Threading.DispatcherPriority.DataBind);

                System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    RebuildCards,
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            var ft = table.Clone();
            foreach (DataRow row in table.Rows)
            {
                decimal total = ReadDec(row, prevCol)
                              + ReadDec(row, quarterlyCol)
                              + ReadDec(row, fineCol);
                bool ok = SelectedFeeFilter == "Pending Fees Only" ? total > 0
                        : SelectedFeeFilter == "No Pending Fees" ? total == 0
                        : true;
                if (ok) ft.ImportRow(row);
            }

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PendingFeesView = null;
                PendingFeesView = new System.Data.DataView(ft);
            }, System.Windows.Threading.DispatcherPriority.DataBind);

            System.Windows.Application.Current.Dispatcher.InvokeAsync(
                RebuildCards,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ═════════════════════════════════════════════════════════════════
        // BUILD CARD LIST  — reads scholarship column and applies discount
        // ═════════════════════════════════════════════════════════════════

        private void RebuildCards()
        {
            FeeStudentCards.Clear();
            if (_fullSheetData == null) return;

            var table = _fullSheetData;

            DataColumn ColFind(params string[] keywords) =>
                table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => keywords.Any(k =>
                        c.ColumnName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

            decimal SafeDec(DataRow r, DataColumn c) =>
                c != null && decimal.TryParse(r[c]?.ToString()?.Trim(), out decimal v) ? v : 0m;

            var nameCol = ColFind("name");
            var fatherCol = ColFind("father");
            var categoryCol = ColFind("category");
            var quarterlyCol = ColFind("quarterly fees") ?? ColFind("installment") ?? ColFind("fees");
            var prevPendCol = ColFind("previous", "pending") ?? ColFind("pending");
            var phoneCol = ColFind("phone") ?? ColFind("contact") ?? ColFind("mobile");
            // ── NEW: scholarship column ──────────────────────────────────
            var scholarCol = ColFind("scholarship");

            var rows = PendingFeesView != null
                ? PendingFeesView.Cast<DataRowView>().Select(drv => drv.Row).ToList()
                : table.Rows.Cast<DataRow>().ToList();

            int serial = 1;
            foreach (var row in rows)
            {
                string nm = nameCol != null ? row[nameCol]?.ToString()?.Trim() ?? "" : "";
                if (string.IsNullOrEmpty(nm)) continue;
                if (nm.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;
                if (nm.Length > 60 || nm.Contains(":-") || nm.Contains("Per Day")) continue;

                decimal quarterlyRaw = SafeDec(row, quarterlyCol);
                decimal scholarshipPct = SafeDec(row, scholarCol);
                // Discounted quarterly = what the student actually owes this quarter
                decimal quarterlyNet = quarterlyRaw * (1m - scholarshipPct / 100m);

                decimal prevPend = SafeDec(row, prevPendCol);
                // TotalDue uses the scholarship-adjusted quarterly fee
                decimal totalDue = quarterlyNet + prevPend;

                string cat = (categoryCol != null
                    ? row[categoryCol]?.ToString()?.Trim() ?? ""
                    : "").ToUpper();

                var sourceRowView = table.DefaultView
                    .Cast<DataRowView>()
                    .FirstOrDefault(drv => drv.Row == row);

                FeeStudentCards.Add(new FeeStudentCard
                {
                    SerialNumber = serial.ToString(),
                    Name = nm,
                    FatherName = fatherCol != null ? row[fatherCol]?.ToString()?.Trim() ?? "–" : "–",
                    PhoneNumber = phoneCol != null ? row[phoneCol]?.ToString()?.Trim() ?? "" : "",
                    Category = cat,
                    QuarterlyFeeRaw = quarterlyRaw,
                    ScholarshipPct = scholarshipPct,
                    PreviousPending = prevPend,
                    TotalDue = totalDue,
                    SourceRow = sourceRowView
                });

                serial++;
            }

            SummaryTotalStudents = FeeStudentCards.Count;
            SummaryPendingStudents = FeeStudentCards.Count(c => c.TotalDue > 0);
            SummaryPaidStudents = FeeStudentCards.Count(c => c.TotalDue <= 0);
            SummaryPendingAmount = FeeStudentCards.Sum(c => c.TotalDue);
        }

        // ═════════════════════════════════════════════════════════════════
        // SELECT STUDENT CARD COMMAND
        // ═════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void SelectStudentCard(FeeStudentCard card)
        {
            if (card == null) return;

            foreach (var c in FeeStudentCards)
                c.IsSelected = false;

            card.IsSelected = true;
            SelectedStudentCard = card;

            if (card.SourceRow != null)
                SelectedRow = card.SourceRow;
        }

        // ═════════════════════════════════════════════════════════════════
        // STUDENT INFO HELPERS  — reads & applies scholarship
        // ═════════════════════════════════════════════════════════════════

        private void ClearStudentInfo()
        {
            StudentName = StudentPhoneNumber = StudentGuardianName =
                StudentId = CurrentQuarter = FineBreakdownText = string.Empty;

            PreviousPendingAmount = QuarterlyFeeRawAmount = ScholarshipPercentage =
                ScholarshipDiscountAmount = QuarterlyFeeAmount =
                CurrentFineAmount = NetFineAfterWaiver = TotalPendingForSelectedStudent = 0;

            FineWaiverAmount = "0";
        }

        private void UpdateSelectedStudentInfo()
        {
            if (SelectedRow == null) return;
            var t = SelectedRow.Row.Table;

            StudentName = ColVal(t, SelectedRow, c => c.Contains("name") && !c.Contains("father"));
            StudentGuardianName = ColVal(t, SelectedRow, c => c.Contains("father") || c.Contains("guardian") || c.Contains("parent"));
            StudentId = ColVal(t, SelectedRow, c => c.Contains("student id") || c.Contains("roll") || c.Contains("reg"));
            StudentPhoneNumber = ColVal(t, SelectedRow, c => c.Contains("phone") || c.Contains("mobile") || c.Contains("contact"));

            var meta = _csvService.GetSheetMetadata(_currentSheetName);
            CurrentQuarter = meta?.Period ?? _currentSheetName;

            var prevCol = FindCol(t, "previous", "pending");
            var quarterlyCol = FindCol(t, "quarterly fees", "installment");
            var fineCol = FindFineCol(t);
            var waiverCol = FindWaiverCol(t);
            // ── NEW: read scholarship column ─────────────────────────────
            var scholarCol = FindCol(t, "scholarship");

            PreviousPendingAmount = ReadDec(SelectedRow.Row, prevCol);
            QuarterlyFeeRawAmount = ReadDec(SelectedRow.Row, quarterlyCol);

            // Calculate scholarship discount and net quarterly fee
            ScholarshipPercentage = scholarCol != null
                                        ? ReadDec(SelectedRow.Row, scholarCol)
                                        : 0m;
            ScholarshipDiscountAmount = QuarterlyFeeRawAmount * (ScholarshipPercentage / 100m);
            QuarterlyFeeAmount = QuarterlyFeeRawAmount - ScholarshipDiscountAmount;

            // Fine only applies if there is actually something pending
            bool hasPending = PreviousPendingAmount > 0 || QuarterlyFeeAmount > 0;
            if (hasPending)
            {
                decimal injectedFine = fineCol != null ? ReadDec(SelectedRow.Row, fineCol) : 0m;
                if (injectedFine > 0)
                {
                    CurrentFineAmount = injectedFine;
                }
                else
                {
                    decimal liveFine = _fineService.Calculate(_currentQuarterStart, DateTime.Now);
                    decimal waived = waiverCol != null ? ReadDec(SelectedRow.Row, waiverCol) : 0m;
                    CurrentFineAmount = Math.Max(0m, liveFine - waived);
                }

                var bd = _fineService.GetBreakdown(_currentQuarterStart, DateTime.Now);
                FineBreakdownText = bd.Summary;
            }
            else
            {
                CurrentFineAmount = 0m;
                FineBreakdownText = "No pending fees — no fine applicable.";
            }

            FineWaiverAmount = "0";
            NetFineAfterWaiver = CurrentFineAmount;
            TotalPendingForSelectedStudent =
                PreviousPendingAmount + QuarterlyFeeAmount + NetFineAfterWaiver;
        }

        // ═════════════════════════════════════════════════════════════════
        // COMMANDS
        // ═════════════════════════════════════════════════════════════════

        [RelayCommand]
        public void ApplyFineWaiver()
        {
            if (SelectedRow == null)
            { MessageBox.Show("Please select a student first.", "No Student Selected", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (FineWaiverAmountDecimal <= 0)
            { MessageBox.Show("Enter a waiver amount greater than zero.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var targetRow = FindTargetRow(_fullSheetData);
            if (targetRow == null)
            { MessageBox.Show("Could not find the student record.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            const string WaiverColName = "Fine Waiver";
            if (!_fullSheetData.Columns.Contains(WaiverColName))
                _fullSheetData.Columns.Add(WaiverColName, typeof(string));

            decimal existingWaiver = ReadDec(targetRow, _fullSheetData.Columns[WaiverColName]);
            decimal newTotalWaiver = existingWaiver + FineWaiverAmountDecimal;
            targetRow[WaiverColName] = newTotalWaiver.ToString("F2");

            var fineCol = FindFineCol(_fullSheetData);
            if (fineCol != null)
                targetRow[fineCol] = NetFineAfterWaiver.ToString("F2");

            var meta = _csvService.GetSheetMetadata(_currentSheetName);
            _paymentLogService.LogPayment(
                studentName: StudentName,
                studentId: StudentId,
                sheetName: _currentSheetName,
                courseName: meta?.CourseInfo ?? _currentSheetName,
                period: meta?.Period ?? "",
                amountPaid: FineWaiverAmountDecimal,
                paymentMode: "Fine Waiver",
                previousBalance: CurrentFineAmount,
                newBalance: NetFineAfterWaiver,
                phoneNumber: StudentPhoneNumber,
                guardianName: StudentGuardianName,
                remarks: $"Fine waiver | Original: Rs{CurrentFineAmount:F2}" +
                         $" | Waiver: Rs{FineWaiverAmountDecimal:F2}" +
                         $" | Net: Rs{NetFineAfterWaiver:F2}"
            );

            MessageBox.Show(
                $"Fine waiver applied!\n\nStudent: {StudentName}\n" +
                $"Original Fine:  Rs{CurrentFineAmount:F2}\n" +
                $"Waiver Applied: Rs{FineWaiverAmountDecimal:F2}\n" +
                $"Net Fine Now:   Rs{NetFineAfterWaiver:F2}\n\n" +
                "Click 'Save Changes' to persist the waiver to disk.",
                "Waiver Applied", MessageBoxButton.OK, MessageBoxImage.Information);

            CurrentFineAmount = NetFineAfterWaiver;
            FineWaiverAmount = "0";
            TotalPendingForSelectedStudent =
                PreviousPendingAmount + QuarterlyFeeAmount + NetFineAfterWaiver;

            ApplyFeeFilter();
        }

        [RelayCommand]
        public void ProcessPayment()
        {
            if (SelectedRow == null)
            { MessageBox.Show("Please select a student first.", "No Student Selected", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (PaymentAmountDecimal <= 0)
            { MessageBox.Show("Please enter a valid payment amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (PaymentAmountDecimal > TotalPendingForSelectedStudent && TotalPendingForSelectedStudent > 0)
            {
                if (MessageBox.Show(
                        $"Payment (Rs{PaymentAmountDecimal:F2}) exceeds pending (Rs{TotalPendingForSelectedStudent:F2}).\nProceed?",
                        "Overpayment", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.No) return;
            }

            try
            {
                decimal previousBalance = TotalPendingForSelectedStudent;
                var table = _fullSheetData;
                var targetRow = FindTargetRow(table);
                if (targetRow == null)
                { MessageBox.Show("Could not find the student record.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

                var prevCol = FindCol(table, "previous", "pending");
                var quarterlyCol = FindCol(table, "quarterly fees", "installment");
                var fineCol = FindFineCol(table);
                var totalCol = FindCol(table, "total");

                decimal remaining = PaymentAmountDecimal, totalApplied = 0;

                // 1. Clear fine first
                if (fineCol != null && remaining > 0)
                {
                    decimal fineAmt = ReadDec(targetRow, fineCol);
                    if (fineAmt > 0)
                    {
                        decimal d = Math.Min(fineAmt, remaining);
                        targetRow[fineCol] = (fineAmt - d).ToString("F2");
                        totalApplied += d; remaining -= d;
                    }
                }
                // 2. Clear previous pending
                if (prevCol != null && remaining > 0)
                {
                    decimal prevAmt = ReadDec(targetRow, prevCol);
                    if (prevAmt > 0)
                    {
                        decimal d = Math.Min(prevAmt, remaining);
                        targetRow[prevCol] = (prevAmt - d).ToString("F2");
                        totalApplied += d; remaining -= d;
                    }
                }
                // 3. Clear quarterly fee
                //    The student's actual obligation is QuarterlyFeeAmount (after scholarship).
                //    The file stores the RAW fee (before scholarship), so we must convert the
                //    net payment back to a raw equivalent before writing it to the file.
                //    Example: raw=9000, scholarship=10%, net due=8100.
                //    Paying 8100 net → raw equivalent = 8100 / 0.90 = 9000 → file becomes 0.
                //    This prevents the "900 raw left → 810 net shown" ghost-balance bug.
                if (quarterlyCol != null && remaining > 0)
                {
                    decimal qAmtDue = QuarterlyFeeAmount;           // scholarship-adjusted due
                    decimal qInFile = ReadDec(targetRow, quarterlyCol); // raw value in CSV
                    if (qAmtDue > 0)
                    {
                        decimal d = Math.Min(qAmtDue, remaining);   // net amount being paid now

                        // Convert the net payment to the raw-file equivalent so the stored
                        // raw balance is reduced by the correct proportion.
                        decimal scholarshipFactor = ScholarshipPercentage > 0
                            ? (1m - ScholarshipPercentage / 100m)
                            : 1m;
                        decimal dRaw = scholarshipFactor > 0
                            ? Math.Round(d / scholarshipFactor, 2)
                            : d;

                        targetRow[quarterlyCol] = Math.Max(0m, qInFile - dRaw).ToString("F2");
                        totalApplied += d; remaining -= d;
                    }
                }

                if (totalApplied == 0)
                { MessageBox.Show("No pending amounts to pay.", "No Fees", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                if (totalCol != null)
                    targetRow[totalCol] = (ReadDec(targetRow, prevCol)
                                         + ReadDec(targetRow, quarterlyCol)
                                         + ReadDec(targetRow, fineCol)).ToString("F2");

                decimal newBalance = previousBalance - totalApplied;
                _csvService.RecordPayment(
                    _currentSheetName, targetRow, totalApplied, SelectedPaymentMode, DateTime.Now);

                var meta = _csvService.GetSheetMetadata(_currentSheetName);
                _paymentLogService.LogPayment(
                    studentName: StudentName,
                    studentId: StudentId,
                    sheetName: _currentSheetName,
                    courseName: meta?.CourseInfo ?? _currentSheetName,
                    period: meta?.Period ?? CurrentQuarter,
                    amountPaid: totalApplied,
                    paymentMode: SelectedPaymentMode,
                    previousBalance: previousBalance,
                    newBalance: newBalance,
                    phoneNumber: StudentPhoneNumber,
                    guardianName: StudentGuardianName,
                    remarks: $"Fee payment | Quarter: {CurrentQuarter}" +
                             $" | Mode: {SelectedPaymentMode}" +
                             (ScholarshipPercentage > 0
                                 ? $" | Scholarship: {ScholarshipPercentage:N0}% (-Rs{ScholarshipDiscountAmount:F2})"
                                 : "") +
                             $" | Prev: Rs{previousBalance:F2}" +
                             $" | New Balance: Rs{newBalance:F2}"
                );

                MessageBox.Show(
                    $"Payment Successful!\n\n" +
                    $"Student:        {StudentName}\nStudent ID:     {StudentId}\n" +
                    $"Guardian:       {StudentGuardianName}\nQuarter:        {CurrentQuarter}\n" +
                    $"Date/Time:      {DateTime.Now:dd-MM-yyyy HH:mm}\n\n" +
                    $"Amount Paid:    Rs{totalApplied:F2}\nPayment Mode:   {SelectedPaymentMode}\n" +
                    (ScholarshipPercentage > 0
                        ? $"Scholarship:    {ScholarshipPercentage:N0}% (saved Rs{ScholarshipDiscountAmount:F2})\n"
                        : "") +
                    $"Previous Total: Rs{previousBalance:F2}\nNew Balance:    Rs{newBalance:F2}\n\n" +
                    $"Transaction logged. View in 'Payment History'.\nClick 'Save Changes' to persist.",
                    "Payment Applied", MessageBoxButton.OK, MessageBoxImage.Information);

                // ── PERSISTENCE FIX: auto-save immediately after every payment ──────
                // Without this, data is lost if the admin closes the app without
                // clicking "Save Changes". The button still works as a manual fallback.
                try
                {
                    RemoveTransientFineColumns();
                    _csvService.SaveFile();
                    if (_fullSheetData != null)
                        _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FeeCollection] Auto-save after payment failed: {saveEx.Message}");
                }

                ApplyFeeFilter();
                PaymentAmount = "0";
                UpdateSelectedStudentInfo();
                App.RaiseFeeDataChanged();
            }
            catch (Exception ex)
            { MessageBox.Show($"Payment processing failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        [RelayCommand]
        public void SendWhatsAppReminder()
        {
            if (SelectedRow == null)
            { MessageBox.Show("Please select a student.", "No Student", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(StudentPhoneNumber))
            { MessageBox.Show("No phone number for this student.", "No Phone", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            string c = StudentPhoneNumber.Replace(" ", "").Replace("-", "").Replace("+", "");
            if (!c.All(char.IsDigit) || c.Length < 10)
            { MessageBox.Show("Invalid phone number.", "Invalid Phone", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!c.StartsWith("91") && c.Length == 10) c = "91" + c;

            string scholarshipLine = ScholarshipPercentage > 0
                ? $"%0AScholarship: {ScholarshipPercentage:N0}% (Discount Rs{ScholarshipDiscountAmount:F2})"
                  + $"%0ANet Quarterly: Rs{QuarterlyFeeAmount:F2}"
                : "";

            string msg = $"Dear {StudentGuardianName},%0A%0AFee reminder for *{StudentName}*" +
                         $"%0A%0AQuarter: {CurrentQuarter}" +
                         $"%0APrevious Pending: Rs{PreviousPendingAmount:F2}" +
                         $"%0AQuarterly Fees: Rs{QuarterlyFeeRawAmount:F2}" +
                         scholarshipLine +
                         $"%0AFine: Rs{NetFineAfterWaiver:F2}" +
                         $"%0A*Total Due: Rs{TotalPendingForSelectedStudent:F2}*" +
                         $"%0A%0APlease pay at the earliest.%0ASchool Administration";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://web.whatsapp.com/send?phone={c}&text={msg}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            { MessageBox.Show($"Failed to open WhatsApp.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void UpdateNoteInformation()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            { HasActiveNote = false; NoteInformation = "No note."; return; }

            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null)
            { HasActiveNote = false; NoteInformation = "No auto-increment note."; ExtensionDate = DateTime.Now.AddMonths(1); return; }

            HasActiveNote = true;
            ExtensionDate = ni.IncrementDate;
            bool past = DateTime.Now >= ni.IncrementDate;
            NoteInformation = $"{(past ? "PAST DUE" : "ACTIVE")}\n\n" +
                              $"Increment: Rs{ni.IncrementAmount}\n" +
                              $"Target: {ni.IncrementDate:dd-MM-yyyy}\n" +
                              $"Days {(past ? "Overdue" : "Remaining")}: {Math.Abs((ni.IncrementDate - DateTime.Now).Days)}";
        }

        [RelayCommand]
        public void UpdateExtensionDate()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            { MessageBox.Show("Please select a sheet first.", "No Sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null)
            { MessageBox.Show("No auto-increment note found.", "No Note", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _csvService.UpdateExtensionDate(_currentSheetName, ExtensionDate);
            MessageBox.Show($"Extension date updated to {ExtensionDate:dd-MM-yyyy}.", "Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateNoteInformation();
        }

        [RelayCommand]
        public void ManualApplyIncrement()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            { MessageBox.Show("Please select a sheet first.", "No Sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null)
            { MessageBox.Show("No auto-increment note found.", "No Note", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show($"Apply increment of Rs{ni.IncrementAmount}? Cannot be undone easily.",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _csvService.ManuallyApplyIncrement(_currentSheetName);
                LoadSheetData(SelectedSheet);
                MessageBox.Show($"Increment of Rs{ni.IncrementAmount} applied.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void SaveChanges()
        {
            try
            {
                RemoveTransientFineColumns();
                _csvService.SaveFile();

                if (_fullSheetData != null)
                    _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);

                MessageBox.Show("Changes saved!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (_fullSheetData != null)
                    _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);

                MessageBox.Show($"Save failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveTransientFineColumns()
        {
            foreach (var sheetName in _csvService.GetSheetNames())
            {
                var tbl = _csvService.GetSheet(sheetName);
                if (tbl != null && tbl.Columns.Contains("Fine"))
                    tbl.Columns.Remove("Fine");
            }
        }

        [RelayCommand]
        public void GoBack() =>
            Application.Current.MainWindow.Content =
                App.Current.Services.GetRequiredService<DashboardView>();

        // ═════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════

        private static DateTime DetermineQuarterStart(DataTable table, string periodString)
        {
            if (table.ExtendedProperties.ContainsKey("QuarterStart") &&
                table.ExtendedProperties["QuarterStart"] is DateTime stored)
                return stored;

            if (!string.IsNullOrWhiteSpace(periodString))
            {
                var parsed = FineCalculationService.TryParseQuarterStart(periodString);
                if (parsed.HasValue)
                {
                    table.ExtendedProperties["QuarterStart"] = parsed.Value;
                    return parsed.Value;
                }
            }
            return new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        }

        private static DataColumn FindCol(DataTable t, params string[] keywords) =>
            t.Columns.Cast<DataColumn>()
             .FirstOrDefault(c => keywords.Any(k =>
                 c.ColumnName.ToLower().Contains(k)));

        private static DataColumn FindFineCol(DataTable t)
        {
            if (t == null) return null;
            if (t.Columns.Contains("Fine")) return t.Columns["Fine"];
            var exact = t.Columns.Cast<DataColumn>()
                         .FirstOrDefault(c => string.Equals(
                             c.ColumnName, "fine", StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return t.Columns.Cast<DataColumn>()
                    .Where(c => c.ColumnName.IndexOf("fine",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(c => c.ColumnName.Length)
                    .FirstOrDefault();
        }

        private static DataColumn FindWaiverCol(DataTable t)
        {
            if (t == null) return null;
            if (t.Columns.Contains("Fine Waiver")) return t.Columns["Fine Waiver"];
            return t.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => string.Equals(
                        c.ColumnName, "fine waiver", StringComparison.OrdinalIgnoreCase));
        }

        private static decimal ReadDec(DataRow row, DataColumn col)
        {
            if (col == null) return 0m;
            return decimal.TryParse(row[col]?.ToString()?.Trim(), out decimal v) ? v : 0m;
        }

        private static string ColVal(DataTable t, DataRowView row, Func<string, bool> pred)
        {
            var col = t.Columns.Cast<DataColumn>()
                       .FirstOrDefault(c => pred(c.ColumnName.ToLower()));
            return col != null ? row[col.ColumnName]?.ToString()?.Trim() ?? "" : "";
        }

        private DataRow FindTargetRow(DataTable table)
        {
            foreach (DataRow row in table.Rows)
            {
                bool match = true;
                int len = Math.Min(row.ItemArray.Length, SelectedRow.Row.ItemArray.Length);
                for (int i = 0; i < len; i++)
                    if (!row[i].Equals(SelectedRow.Row[i])) { match = false; break; }
                if (match) return row;
            }
            return null;
        }
    }
}