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
        private DataTable _fullSheetData;
        private string _currentSheetName;

        public ObservableCollection<string> SheetNames { get; } = new();
        public ObservableCollection<string> FilteredSheetNames { get; } = new();

        public ObservableCollection<string> PaymentModes { get; } = new()
        {
            "Cash",
            "UPI",
            "Net Banking",
            "Credit Card",
            "Debit Card",
            "Cheque"
        };

        public ObservableCollection<string> FeeFilterOptions { get; } = new()
        {
            "All Students",
            "Pending Fees Only",
            "No Pending Fees"
        };

        [ObservableProperty]
        private string selectedSheet;

        [ObservableProperty]
        private string sheetSearchText;

        [ObservableProperty]
        private DataView pendingFeesView;

        [ObservableProperty]
        private DataRowView selectedRow;

        [ObservableProperty]
        private decimal paymentAmount;

        [ObservableProperty]
        private string selectedPaymentMode = "Cash";

        [ObservableProperty]
        private decimal totalPendingForSelectedStudent;

        [ObservableProperty]
        private string studentPhoneNumber;

        [ObservableProperty]
        private string studentName;

        [ObservableProperty]
        private string selectedFeeFilter = "All Students";

        [ObservableProperty]
        private string noteInformation;

        [ObservableProperty]
        private DateTime extensionDate = DateTime.Now.AddMonths(1);

        [ObservableProperty]
        private bool hasActiveNote;

        public FeeCollectionViewModel(CsvDataService csvService, PaymentLogService paymentLogService)
        {
            _csvService = csvService;
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
            if (!string.IsNullOrEmpty(value))
            {
                LoadSheetData(value);
                UpdateNoteInformation();
            }
        }

        partial void OnSelectedFeeFilterChanged(string value)
        {
            ApplyFeeFilter();
        }

        private void LoadSheetData(string displayName)
        {
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);
            ApplyFeeFilter();
        }

        private void ApplyFeeFilter()
        {
            if (_fullSheetData == null) return;

            var table = _fullSheetData;

            // Get Previous Pending and Quarterly Fees columns (NOT Balance)
            var previousPendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending"));

            var quarterlyFeesCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly fees") ||
                                   c.ColumnName.ToLower().Contains("installment"));

            if ((previousPendingCol == null && quarterlyFeesCol == null) || SelectedFeeFilter == "All Students")
            {
                PendingFeesView = table.DefaultView;
                return;
            }

            var filteredTable = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                // Calculate total pending from Previous Pending + Quarterly Fees
                decimal totalPending = 0;

                if (previousPendingCol != null)
                {
                    string prevRaw = row[previousPendingCol]?.ToString()?.Trim();
                    if (decimal.TryParse(prevRaw, out decimal prevAmount) && prevAmount > 0)
                    {
                        totalPending += prevAmount;
                    }
                }

                if (quarterlyFeesCol != null)
                {
                    string quarterlyRaw = row[quarterlyFeesCol]?.ToString()?.Trim();
                    if (decimal.TryParse(quarterlyRaw, out decimal quarterlyAmount) && quarterlyAmount > 0)
                    {
                        totalPending += quarterlyAmount;
                    }
                }

                bool shouldInclude = false;

                if (SelectedFeeFilter == "Pending Fees Only" && totalPending > 0)
                    shouldInclude = true;
                else if (SelectedFeeFilter == "No Pending Fees" && totalPending == 0)
                    shouldInclude = true;

                if (shouldInclude)
                    filteredTable.ImportRow(row);
            }

            PendingFeesView = filteredTable.DefaultView;
        }

        partial void OnSelectedRowChanged(DataRowView value)
        {
            if (value != null)
            {
                UpdateSelectedStudentInfo();
            }
            else
            {
                TotalPendingForSelectedStudent = 0;
                StudentPhoneNumber = string.Empty;
                StudentName = string.Empty;
            }
        }

        private void UpdateSelectedStudentInfo()
        {
            if (SelectedRow == null) return;

            var table = SelectedRow.Row.Table;

            var nameCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("name") &&
                                   !c.ColumnName.ToLower().Contains("father"));

            if (nameCol != null)
                StudentName = SelectedRow[nameCol.ColumnName]?.ToString()?.Trim() ?? "Unknown";

            var phoneCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("phone") ||
                                   c.ColumnName.ToLower().Contains("mobile") ||
                                   c.ColumnName.ToLower().Contains("contact"));

            if (phoneCol != null)
                StudentPhoneNumber = SelectedRow[phoneCol.ColumnName]?.ToString()?.Trim() ?? "";

            // Calculate total from Previous Pending + Quarterly Fees (NOT Balance)
            var previousPendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending"));

            var quarterlyFeesCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly fees") ||
                                   c.ColumnName.ToLower().Contains("installment"));

            decimal totalPending = 0;

            // Add previous pending
            if (previousPendingCol != null)
            {
                string prevRaw = SelectedRow[previousPendingCol.ColumnName]?.ToString()?.Trim();
                if (decimal.TryParse(prevRaw, out decimal prevAmount) && prevAmount > 0)
                {
                    totalPending += prevAmount;
                }
            }

            // Add quarterly fees
            if (quarterlyFeesCol != null)
            {
                string quarterlyRaw = SelectedRow[quarterlyFeesCol.ColumnName]?.ToString()?.Trim();
                if (decimal.TryParse(quarterlyRaw, out decimal quarterlyAmount) && quarterlyAmount > 0)
                {
                    totalPending += quarterlyAmount;
                }
            }

            TotalPendingForSelectedStudent = totalPending;
        }

        private void UpdateNoteInformation()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            {
                HasActiveNote = false;
                NoteInformation = "No note information available.";
                return;
            }

            var noteInfo = _csvService.GetSheetNote(_currentSheetName);

            if (noteInfo == null)
            {
                HasActiveNote = false;
                NoteInformation = "No auto-increment note found for this sheet.";
                ExtensionDate = DateTime.Now.AddMonths(1);
            }
            else
            {
                HasActiveNote = true;
                ExtensionDate = noteInfo.IncrementDate;

                bool isPastDue = DateTime.Now >= noteInfo.IncrementDate;
                string status = isPastDue ? "⚠️ PAST DUE" : "✅ ACTIVE";

                NoteInformation = $"{status}\n\n" +
                    $"Increment Amount: ₹{noteInfo.IncrementAmount}\n" +
                    $"Target Date: {noteInfo.IncrementDate:dd-MM-yyyy}\n" +
                    $"Days {(isPastDue ? "Overdue" : "Remaining")}: {Math.Abs((noteInfo.IncrementDate - DateTime.Now).Days)}";
            }
        }

        [RelayCommand]
        public void ProcessPayment()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("⚠️ Please select a student first.", "No Student Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (PaymentAmount <= 0)
            {
                MessageBox.Show("⚠️ Please enter a valid payment amount.", "Invalid Amount",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Allow overpayment with warning
            if (PaymentAmount > TotalPendingForSelectedStudent && TotalPendingForSelectedStudent > 0)
            {
                var result = MessageBox.Show(
                    $"⚠️ Payment amount (₹{PaymentAmount:F2}) exceeds pending fees (₹{TotalPendingForSelectedStudent:F2}).\n\n" +
                    "Do you want to proceed anyway?",
                    "Overpayment Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                    return;
            }

            try
            {
                decimal previousBalance = TotalPendingForSelectedStudent;

                // Find the target row
                var table = _fullSheetData;
                DataRow targetRow = null;

                foreach (DataRow row in table.Rows)
                {
                    bool match = true;
                    for (int i = 0; i < Math.Min(row.ItemArray.Length, SelectedRow.Row.ItemArray.Length); i++)
                    {
                        if (!row[i].Equals(SelectedRow.Row[i]))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        targetRow = row;
                        break;
                    }
                }

                if (targetRow == null)
                {
                    MessageBox.Show("❌ Could not find the student record.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Find Previous Pending and Quarterly Fees columns
                var previousPendingCol = table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                       c.ColumnName.ToLower().Contains("pending"));

                var quarterlyFeesCol = table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly fees") ||
                                       c.ColumnName.ToLower().Contains("installment"));

                // Apply payment: Previous Pending FIRST, then Quarterly Fees
                decimal remaining = PaymentAmount;
                decimal totalApplied = 0;

                // Step 1: Deduct from Previous Pending first
                if (previousPendingCol != null && remaining > 0)
                {
                    string prevRaw = targetRow[previousPendingCol]?.ToString()?.Trim();
                    if (decimal.TryParse(prevRaw, out decimal previousAmount) && previousAmount > 0)
                    {
                        if (previousAmount <= remaining)
                        {
                            // Pay off all previous pending
                            targetRow[previousPendingCol] = "0.00";
                            totalApplied += previousAmount;
                            remaining -= previousAmount;
                        }
                        else
                        {
                            // Partial payment on previous pending
                            targetRow[previousPendingCol] = (previousAmount - remaining).ToString("F2");
                            totalApplied += remaining;
                            remaining = 0;
                        }
                    }
                }

                // Step 2: If remaining, deduct from Quarterly Fees
                if (quarterlyFeesCol != null && remaining > 0)
                {
                    string quarterlyRaw = targetRow[quarterlyFeesCol]?.ToString()?.Trim();
                    if (decimal.TryParse(quarterlyRaw, out decimal quarterlyAmount) && quarterlyAmount > 0)
                    {
                        if (quarterlyAmount <= remaining)
                        {
                            // Pay off all quarterly fees
                            targetRow[quarterlyFeesCol] = "0.00";
                            totalApplied += quarterlyAmount;
                            remaining -= quarterlyAmount;
                        }
                        else
                        {
                            // Partial payment on quarterly fees
                            targetRow[quarterlyFeesCol] = (quarterlyAmount - remaining).ToString("F2");
                            totalApplied += remaining;
                            remaining = 0;
                        }
                    }
                }

                // Check if payment was applied
                if (totalApplied == 0)
                {
                    MessageBox.Show(
                        "⚠️ No payment could be applied.\n\n" +
                        "This student has no pending fees in the 'Previous Pending' or 'Quarterly Fees' columns.",
                        "No Fees to Pay",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // ✅ NEW: Record payment in payment history
                _csvService.RecordPayment(_currentSheetName, targetRow, totalApplied, SelectedPaymentMode, DateTime.Now);

                // Recalculate totals and balance
                _csvService.RecalculateRowFees(_currentSheetName, targetRow);

                decimal newBalance = previousBalance - totalApplied;

                // Get metadata for logging
                var metadata = _csvService.GetSheetMetadata(_currentSheetName);
                string courseName = metadata?.CourseInfo ?? _currentSheetName;
                string period = metadata?.Period ?? "";

                // Log payment
                _paymentLogService.LogPayment(
                    studentName: StudentName,
                    sheetName: _currentSheetName,
                    courseName: courseName,
                    period: period,
                    amountPaid: totalApplied,
                    paymentMode: SelectedPaymentMode,
                    previousBalance: previousBalance,
                    newBalance: newBalance,
                    phoneNumber: StudentPhoneNumber,
                    remarks: $"Payment processed via Fee Collection module"
                );

                // Success message
                MessageBox.Show(
                    $"✅ Payment Successful!\n\n" +
                    $"Student: {StudentName}\n" +
                    $"Amount Paid: ₹{totalApplied:F2}\n" +
                    $"Payment Mode: {SelectedPaymentMode}\n" +
                    $"Previous Balance: ₹{previousBalance:F2}\n" +
                    $"New Balance: ₹{newBalance:F2}\n\n" +
                    $"📋 Transaction logged and payment history updated.\n\n" +
                    $"Note: Save changes to persist the payment.",
                    "Payment Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh view
                ApplyFeeFilter();
                PaymentAmount = 0;
                UpdateSelectedStudentInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Payment processing failed: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void SendWhatsAppReminder()
        {
            if (SelectedRow == null)
            {
                MessageBox.Show("⚠️ Please select a student first.", "No Student Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(StudentPhoneNumber))
            {
                MessageBox.Show(
                    "❌ No phone number found for this student.\n\n" +
                    "Please add the phone number in the 'Manage Students' section first.",
                    "No Phone Number",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string cleanedPhone = StudentPhoneNumber.Replace(" ", "").Replace("-", "").Replace("+", "");

            if (!cleanedPhone.All(char.IsDigit) || cleanedPhone.Length < 10)
            {
                MessageBox.Show(
                    "⚠️ Invalid phone number format.\n\n" +
                    "Please update the phone number in 'Manage Students' section.",
                    "Invalid Phone Number",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!cleanedPhone.StartsWith("91") && cleanedPhone.Length == 10)
                cleanedPhone = "91" + cleanedPhone;

            string message = $"Dear {StudentName},%0A%0A" +
                           $"This is a reminder regarding pending school fees.%0A%0A" +
                           $"Pending Amount: ₹{TotalPendingForSelectedStudent:F2}%0A%0A" +
                           $"Please make the payment at your earliest convenience.%0A%0A" +
                           $"Thank you!%0A" +
                           $"School Administration";

            string whatsappUrl = $"https://web.whatsapp.com/send?phone={cleanedPhone}&text={message}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = whatsappUrl,
                    UseShellExecute = true
                });

                MessageBox.Show(
                    $"✅ WhatsApp reminder opened!\n\n" +
                    $"Student: {StudentName}\n" +
                    $"Phone: {StudentPhoneNumber}\n" +
                    $"Pending Amount: ₹{TotalPendingForSelectedStudent:F2}\n\n" +
                    $"Please review and send the message from WhatsApp Web.",
                    "WhatsApp Reminder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to open WhatsApp.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"Please ensure you have WhatsApp Web access or try again.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void UpdateExtensionDate()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            {
                MessageBox.Show("⚠️ Please select a sheet first.", "No Sheet Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var noteInfo = _csvService.GetSheetNote(_currentSheetName);

            if (noteInfo == null)
            {
                MessageBox.Show(
                    "ℹ️ No auto-increment note found for this sheet.\n\n" +
                    "Auto-increment notes should be in the format:\n" +
                    "Note: Increment ₹500 after 15-04-2025",
                    "No Note Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _csvService.UpdateExtensionDate(_currentSheetName, ExtensionDate);

            MessageBox.Show(
                $"✅ Extension date updated!\n\n" +
                $"New Date: {ExtensionDate:dd-MM-yyyy}\n" +
                $"Increment Amount: ₹{noteInfo.IncrementAmount}\n\n" +
                $"The auto-increment will be applied on the new date.",
                "Date Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            UpdateNoteInformation();
        }

        [RelayCommand]
        public void ManualApplyIncrement()
        {
            if (string.IsNullOrEmpty(SelectedSheet))
            {
                MessageBox.Show("⚠️ Please select a sheet first.", "No Sheet Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var noteInfo = _csvService.GetSheetNote(_currentSheetName);

            if (noteInfo == null)
            {
                MessageBox.Show(
                    "ℹ️ No auto-increment note found for this sheet.",
                    "No Note Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to apply the increment now?\n\n" +
                $"Increment Amount: ₹{noteInfo.IncrementAmount}\n" +
                $"This will increase all pending fees by this amount.\n\n" +
                $"This action cannot be undone easily.",
                "Confirm Manual Increment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _csvService.ManuallyApplyIncrement(_currentSheetName);
                LoadSheetData(SelectedSheet);

                MessageBox.Show(
                    $"✅ Increment applied successfully!\n\n" +
                    $"All pending fees have been increased by ₹{noteInfo.IncrementAmount}",
                    "Increment Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void SaveChanges()
        {
            try
            {
                _csvService.SaveFile();
                MessageBox.Show(
                    "✅ Changes saved successfully!",
                    "Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Failed to save changes:\n\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}