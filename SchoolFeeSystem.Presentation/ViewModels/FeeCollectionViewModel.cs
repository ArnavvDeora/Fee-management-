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

            // CRITICAL FIX: Use GetSheetDisplayNames instead of GetSheetNames to avoid WPF binding errors
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
            // CRITICAL FIX: Convert display name to actual sheet name
            _currentSheetName = _csvService.GetSheetNameFromDisplay(displayName);
            _fullSheetData = _csvService.GetSheet(_currentSheetName);
            ApplyFeeFilter();
        }

        private void ApplyFeeFilter()
        {
            if (_fullSheetData == null) return;

            var table = _fullSheetData;

            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("due"));

            if (pendingCol == null || SelectedFeeFilter == "All Students")
            {
                PendingFeesView = table.DefaultView;
                return;
            }

            var filteredTable = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                string raw = row[pendingCol]?.ToString()?.Trim();

                if (!decimal.TryParse(raw, out decimal pending))
                    pending = 0;

                bool shouldInclude = false;

                if (SelectedFeeFilter == "Pending Fees Only" && pending > 0)
                    shouldInclude = true;
                else if (SelectedFeeFilter == "No Pending Fees" && pending == 0)
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

            var pendingColumns = table.Columns
                .Cast<DataColumn>()
                .Where(c => c.ColumnName.ToLower().Contains("pending") ||
                           c.ColumnName.ToLower().Contains("previous") ||
                           c.ColumnName.ToLower().Contains("due"))
                .ToList();

            decimal totalPending = 0;
            foreach (var col in pendingColumns)
            {
                string raw = SelectedRow[col.ColumnName]?.ToString()?.Trim();
                if (decimal.TryParse(raw, out decimal amount) && amount > 0)
                {
                    totalPending += amount;
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

            if (PaymentAmount > TotalPendingForSelectedStudent)
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
                // Store previous balance for logging
                decimal previousBalance = TotalPendingForSelectedStudent;

                // Find and update the row
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

                // Find pending columns
                var pendingColumns = table.Columns
                    .Cast<DataColumn>()
                    .Where(c => c.ColumnName.ToLower().Contains("pending") ||
                               c.ColumnName.ToLower().Contains("previous") ||
                               c.ColumnName.ToLower().Contains("due"))
                    .ToList();

                // Apply payment
                decimal remaining = PaymentAmount;
                decimal totalApplied = 0;

                foreach (var col in pendingColumns)
                {
                    if (remaining <= 0) break;

                    string raw = targetRow[col]?.ToString()?.Trim();
                    if (!decimal.TryParse(raw, out decimal current) || current <= 0)
                        continue;

                    if (current <= remaining)
                    {
                        targetRow[col] = "0.00";
                        totalApplied += current;
                        remaining -= current;
                    }
                    else
                    {
                        targetRow[col] = (current - remaining).ToString("F2");
                        totalApplied += remaining;
                        remaining = 0;
                    }
                }

                // Recalculate total fees
                _csvService.RecalculateRowFees(_currentSheetName, targetRow);

                // Calculate new balance
                decimal newBalance = previousBalance - totalApplied;

                // Get metadata for logging
                var metadata = _csvService.GetSheetMetadata(_currentSheetName);
                string courseName = metadata?.CourseInfo ?? _currentSheetName;
                string period = metadata?.Period ?? "";

                // LOG THE PAYMENT TRANSACTION
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
                    $"📋 Transaction logged for reporting.\n\n" +
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