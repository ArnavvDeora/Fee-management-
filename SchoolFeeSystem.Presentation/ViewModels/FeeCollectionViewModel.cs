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
    public partial class FeeCollectionViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;
        private readonly PaymentLogService _paymentLogService;
        private readonly AcademicCycleService _cycleService;
        private readonly FineCalculationService _fineService;   // NEW

        private DataTable _fullSheetData;
        private string _currentSheetName;
        private DateTime _currentQuarterStart;  // NEW: tracked per sheet

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

        [ObservableProperty] private string selectedSheet;
        [ObservableProperty] private string sheetSearchText;
        [ObservableProperty] private DataView pendingFeesView;
        [ObservableProperty] private DataRowView selectedRow;
        [ObservableProperty] private string selectedFeeFilter = "All Students";

        // PaymentAmount is stored as string so the TextBox binding never throws
        // FormatException when the field is cleared (empty string → decimal fails).
        // Use the safe decimal accessor PaymentAmountDecimal in all logic.
        [ObservableProperty] private string paymentAmount = "0";

        /// <summary>Safe decimal accessor — returns 0 if the TextBox is empty or invalid.</summary>
        private decimal PaymentAmountDecimal =>
            decimal.TryParse(PaymentAmount, out decimal v) ? v : 0m;
        [ObservableProperty] private string selectedPaymentMode = "Cash";

        [ObservableProperty] private string studentName;
        [ObservableProperty] private string studentPhoneNumber;
        [ObservableProperty] private string studentGuardianName;
        [ObservableProperty] private string studentId;
        [ObservableProperty] private string currentQuarter;

        [ObservableProperty] private decimal previousPendingAmount;
        [ObservableProperty] private decimal quarterlyFeeAmount;
        [ObservableProperty] private decimal currentFineAmount;
        // FineWaiverAmount also stored as string for the same reason as PaymentAmount.
        [ObservableProperty] private string fineWaiverAmount = "0";

        private decimal FineWaiverAmountDecimal =>
            decimal.TryParse(FineWaiverAmount, out decimal v) ? v : 0m;
        [ObservableProperty] private decimal netFineAfterWaiver;
        [ObservableProperty] private decimal totalPendingForSelectedStudent;
        [ObservableProperty] private string fineBreakdownText;  // NEW: shown in UI

        [ObservableProperty] private string noteInformation;
        [ObservableProperty] private DateTime extensionDate = DateTime.Now.AddMonths(1);
        [ObservableProperty] private bool hasActiveNote;

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
                    "Unpaid balances have been carried forward.",
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

        partial void OnSheetSearchTextChanged(string value)
        {
            FilteredSheetNames.Clear();
            var src = string.IsNullOrWhiteSpace(value)
                ? SheetNames
                : (IEnumerable<string>)SheetNames.Where(n => n.ToLower().Contains(value.ToLower()));
            foreach (var n in src) FilteredSheetNames.Add(n);
        }

        partial void OnSelectedSheetChanged(string value)
        {
            if (!string.IsNullOrEmpty(value)) { LoadSheetData(value); UpdateNoteInformation(); }
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

        private void LoadSheetData(string displayName)
        {
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);

            if (_fullSheetData != null)
            {
                var meta = _csvService.GetSheetMetadata(_currentSheetName);
                _currentQuarterStart = DetermineQuarterStart(_fullSheetData, meta?.Period);

                // Inject correct fines using FineCalculationService
                _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);
            }

            ApplyFeeFilter();
        }

        private void ApplyFeeFilter()
        {
            if (_fullSheetData == null) return;
            var table = _fullSheetData;
            var prevCol = FindCol(table, "previous", "pending");
            var quarterlyCol = FindCol(table, "quarterly fees", "installment");
            // BUG FIX: include the fine column in the "has pending" check so students
            // with an outstanding fine are never silently excluded from the view.
            var fineCol = FindFineCol(table);

            if ((prevCol == null && quarterlyCol == null) || SelectedFeeFilter == "All Students")
            {
                // ULTIMATE FIX: Use Dispatcher to delay binding until WPF finishes processing current state
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PendingFeesView = null;
                    PendingFeesView = new System.Data.DataView(table);
                }, System.Windows.Threading.DispatcherPriority.DataBind);
                return;
            }

            var ft = table.Clone();
            foreach (DataRow row in table.Rows)
            {
                decimal total = ReadDec(row, prevCol) + ReadDec(row, quarterlyCol)
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
        }

        partial void OnSelectedRowChanged(DataRowView value)
        {
            if (value != null) UpdateSelectedStudentInfo();
            else ClearStudentInfo();
        }

        private void ClearStudentInfo()
        {
            StudentName = StudentPhoneNumber = StudentGuardianName =
            StudentId = CurrentQuarter = FineBreakdownText = string.Empty;
            PreviousPendingAmount = QuarterlyFeeAmount = CurrentFineAmount =
            PreviousPendingAmount = QuarterlyFeeAmount = CurrentFineAmount =
            NetFineAfterWaiver = TotalPendingForSelectedStudent = 0;
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
            var fineCol = FindFineCol(t);                    // exact "Fine" column
            var waiverCol = FindWaiverCol(t);                  // "Fine Waiver" column

            PreviousPendingAmount = ReadDec(SelectedRow.Row, prevCol);
            QuarterlyFeeAmount = ReadDec(SelectedRow.Row, quarterlyCol);

            bool hasPending = PreviousPendingAmount > 0 || QuarterlyFeeAmount > 0;
            if (hasPending)
            {
                // Prefer the already-injected "Fine" cell value (includes waiver subtraction
                // applied by InjectFinesIntoTable).  Fall back to live calculation minus
                // any stored waiver if the column hasn't been injected yet on this row.
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
                if (MessageBox.Show($"Payment (Rs{PaymentAmountDecimal:F2}) exceeds pending (Rs{TotalPendingForSelectedStudent:F2}).\nProceed?",
                    "Overpayment", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No) return;
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
                // BUG FIX: Use FindFineCol to get the exact injected "Fine" column
                var fineCol = FindFineCol(table);
                var totalCol = FindCol(table, "total");

                decimal remaining = PaymentAmountDecimal, totalApplied = 0;

                // Fine first
                if (fineCol != null && remaining > 0)
                {
                    decimal fineAmt = ReadDec(targetRow, fineCol);
                    if (fineAmt > 0) { decimal d = Math.Min(fineAmt, remaining); targetRow[fineCol] = (fineAmt - d).ToString("F2"); totalApplied += d; remaining -= d; }
                }
                // Previous pending
                if (prevCol != null && remaining > 0)
                {
                    decimal prevAmt = ReadDec(targetRow, prevCol);
                    if (prevAmt > 0) { decimal d = Math.Min(prevAmt, remaining); targetRow[prevCol] = (prevAmt - d).ToString("F2"); totalApplied += d; remaining -= d; }
                }
                // Quarterly fees
                if (quarterlyCol != null && remaining > 0)
                {
                    decimal qAmt = ReadDec(targetRow, quarterlyCol);
                    if (qAmt > 0) { decimal d = Math.Min(qAmt, remaining); targetRow[quarterlyCol] = (qAmt - d).ToString("F2"); totalApplied += d; remaining -= d; }
                }

                if (totalApplied == 0)
                { MessageBox.Show("No pending amounts to pay.", "No Fees", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                if (totalCol != null)
                    targetRow[totalCol] = (ReadDec(targetRow, prevCol) + ReadDec(targetRow, quarterlyCol) + ReadDec(targetRow, fineCol)).ToString("F2");

                decimal newBalance = previousBalance - totalApplied;
                _csvService.RecordPayment(_currentSheetName, targetRow, totalApplied, SelectedPaymentMode, DateTime.Now);

                var meta = _csvService.GetSheetMetadata(_currentSheetName);

                // Pass studentName, studentId, and guardianName as their own
                // dedicated parameters so PaymentLogService writes them into
                // separate "Student Name" and "Student ID" CSV columns.
                // This is what makes Payment History searchable by name/ID and
                // lets the receipt show the correct student.
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
                                     $" | Prev: Rs{previousBalance:F2}" +
                                     $" | New Balance: Rs{newBalance:F2}"
                );

                MessageBox.Show(
                    $"Payment Successful!\n\n" +
                    $"Student:        {StudentName}\nStudent ID:     {StudentId}\n" +
                    $"Guardian:       {StudentGuardianName}\nQuarter:        {CurrentQuarter}\n" +
                    $"Date/Time:      {DateTime.Now:dd-MM-yyyy HH:mm}\n\n" +
                    $"Amount Paid:    Rs{totalApplied:F2}\nPayment Mode:   {SelectedPaymentMode}\n" +
                    $"Previous Total: Rs{previousBalance:F2}\nNew Balance:    Rs{newBalance:F2}\n\n" +
                    $"Transaction logged. View in 'Payment History'.\nClick 'Save Changes' to persist.",
                    "Payment Applied", MessageBoxButton.OK, MessageBoxImage.Information);

                ApplyFeeFilter();
                PaymentAmount = "0";
                UpdateSelectedStudentInfo();
            }
            catch (Exception ex)
            { MessageBox.Show($"Payment processing failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        [RelayCommand]
        public void SendWhatsAppReminder()
        {
            if (SelectedRow == null) { MessageBox.Show("Please select a student.", "No Student", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(StudentPhoneNumber)) { MessageBox.Show("No phone number for this student.", "No Phone", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            string c = StudentPhoneNumber.Replace(" ", "").Replace("-", "").Replace("+", "");
            if (!c.All(char.IsDigit) || c.Length < 10) { MessageBox.Show("Invalid phone number.", "Invalid Phone", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!c.StartsWith("91") && c.Length == 10) c = "91" + c;
            string msg = $"Dear {StudentGuardianName},%0A%0AFee reminder for *{StudentName}*%0A%0AQuarter: {CurrentQuarter}%0APrevious Pending: Rs{PreviousPendingAmount:F2}%0AQuarterly Fees: Rs{QuarterlyFeeAmount:F2}%0AFine: Rs{NetFineAfterWaiver:F2}%0A*Total Due: Rs{TotalPendingForSelectedStudent:F2}*%0A%0APlease pay at the earliest.%0ASchool Administration";
            try { Process.Start(new ProcessStartInfo { FileName = $"https://web.whatsapp.com/send?phone={c}&text={msg}", UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open WhatsApp.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void UpdateNoteInformation()
        {
            if (string.IsNullOrEmpty(SelectedSheet)) { HasActiveNote = false; NoteInformation = "No note."; return; }
            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null) { HasActiveNote = false; NoteInformation = "No auto-increment note."; ExtensionDate = DateTime.Now.AddMonths(1); return; }
            HasActiveNote = true; ExtensionDate = ni.IncrementDate;
            bool past = DateTime.Now >= ni.IncrementDate;
            NoteInformation = $"{(past ? "PAST DUE" : "ACTIVE")}\n\nIncrement: Rs{ni.IncrementAmount}\nTarget: {ni.IncrementDate:dd-MM-yyyy}\nDays {(past ? "Overdue" : "Remaining")}: {Math.Abs((ni.IncrementDate - DateTime.Now).Days)}";
        }

        [RelayCommand]
        public void UpdateExtensionDate()
        {
            if (string.IsNullOrEmpty(SelectedSheet)) { MessageBox.Show("Please select a sheet first.", "No Sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null) { MessageBox.Show("No auto-increment note found.", "No Note", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _csvService.UpdateExtensionDate(_currentSheetName, ExtensionDate);
            MessageBox.Show($"Extension date updated to {ExtensionDate:dd-MM-yyyy}.", "Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateNoteInformation();
        }

        [RelayCommand]
        public void ManualApplyIncrement()
        {
            if (string.IsNullOrEmpty(SelectedSheet)) { MessageBox.Show("Please select a sheet first.", "No Sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var ni = _csvService.GetSheetNote(_currentSheetName);
            if (ni == null) { MessageBox.Show("No auto-increment note found.", "No Note", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show($"Apply increment of Rs{ni.IncrementAmount}? Cannot be undone easily.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { _csvService.ManuallyApplyIncrement(_currentSheetName); LoadSheetData(SelectedSheet); MessageBox.Show($"Increment of Rs{ni.IncrementAmount} applied.", "Done", MessageBoxButton.OK, MessageBoxImage.Information); }
        }

        [RelayCommand]
        public void SaveChanges()
        {
            // The "Fine" column is injected in-memory on every sheet load and must
            // NOT be written to disk (it causes "Empty extension is not supported"
            // because some rows store empty strings instead of numbers).
            // "Fine Waiver" IS a persistent column and stays on disk.
            // Strategy: strip "Fine" from every in-memory table → save → re-inject.
            try
            {
                RemoveTransientFineColumns();
                _csvService.SaveFile();

                // Re-inject so the grid is still correct after saving.
                if (_fullSheetData != null)
                    _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);

                MessageBox.Show("Changes saved!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Re-inject even on failure so the UI is not left column-less.
                if (_fullSheetData != null)
                    _fineService.InjectFinesIntoTable(_fullSheetData, _currentQuarterStart);

                MessageBox.Show($"Save failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Removes the transient in-memory "Fine" column from every sheet so
        /// SaveFile() only writes columns that belong on disk.
        /// "Fine Waiver" is intentionally left intact — it is a persistent column.
        /// </summary>
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
        public void GoBack() => Application.Current.MainWindow.Content = App.Current.Services.GetRequiredService<DashboardView>();

        // ── Helpers ───────────────────────────────────────────────────────────

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
            t.Columns.Cast<DataColumn>().FirstOrDefault(c => keywords.Any(k => c.ColumnName.ToLower().Contains(k)));

        /// <summary>
        /// Locates the injected fine column added by FineCalculationService.InjectFinesIntoTable().
        /// Strategy (in priority order):
        ///   1. Exact match on "Fine" (the column name written by InjectFinesIntoTable).
        ///   2. Column whose name is EXACTLY "fine" (case-insensitive).
        ///   3. Shortest column name that contains "fine" – avoids matching long descriptive
        ///      columns such as "Remarks / Previous Quarter Late Fees Fine" which caused the
        ///      bug where ReadDec() always returned 0 for the selected student's fine.
        /// </summary>
        private static DataColumn FindFineCol(DataTable t)
        {
            if (t == null) return null;

            // 1. Exact name "Fine" – what InjectFinesIntoTable creates
            if (t.Columns.Contains("Fine"))
                return t.Columns["Fine"];

            // 2. Case-insensitive exact match
            var exact = t.Columns.Cast<DataColumn>()
                         .FirstOrDefault(c => string.Equals(c.ColumnName, "fine",
                                              StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // 3. Shortest column containing "fine" — guards against matching remarks columns
            return t.Columns.Cast<DataColumn>()
                    .Where(c => c.ColumnName.IndexOf("fine", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(c => c.ColumnName.Length)
                    .FirstOrDefault();
        }

        /// <summary>
        /// Locates the persistent "Fine Waiver" column written by ApplyFineWaiver().
        /// This column is saved to disk and survives reloads.
        /// Returns null if the column does not yet exist for this sheet.
        /// </summary>
        private static DataColumn FindWaiverCol(DataTable t)
        {
            if (t == null) return null;
            if (t.Columns.Contains("Fine Waiver"))
                return t.Columns["Fine Waiver"];
            return t.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => string.Equals(c.ColumnName, "fine waiver",
                                         StringComparison.OrdinalIgnoreCase));
        }

        private static decimal ReadDec(DataRow row, DataColumn col)
        {
            if (col == null) return 0m;
            return decimal.TryParse(row[col]?.ToString()?.Trim(), out decimal v) ? v : 0m;
        }

        private static string ColVal(DataTable t, DataRowView row, Func<string, bool> pred)
        {
            var col = t.Columns.Cast<DataColumn>().FirstOrDefault(c => pred(c.ColumnName.ToLower()));
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