using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SchoolFeeSystem.Presentation.Services
{
    /// <summary>
    /// Service to log all payment transactions for reporting and audit purposes
    /// </summary>
    public class PaymentLogService
    {
        private readonly string _logFilePath;
        private readonly string _appDataPath;

        public class PaymentLog
        {
            public string TransactionId { get; set; }
            public DateTime PaymentDate { get; set; }
            public string StudentName { get; set; }
            public string SheetName { get; set; }
            public string CourseName { get; set; }
            public string Period { get; set; }
            public decimal AmountPaid { get; set; }
            public string PaymentMode { get; set; }
            public decimal PreviousBalance { get; set; }
            public decimal NewBalance { get; set; }
            public string PhoneNumber { get; set; }
            public string ProcessedBy { get; set; }
            public string Remarks { get; set; }
        }

        public PaymentLogService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem"
            );

            if (!Directory.Exists(_appDataPath))
                Directory.CreateDirectory(_appDataPath);

            _logFilePath = Path.Combine(_appDataPath, "payment_logs.json");

            // Create file if it doesn't exist
            if (!File.Exists(_logFilePath))
            {
                File.WriteAllText(_logFilePath, "[]");
            }
        }

        /// <summary>
        /// Log a payment transaction
        /// </summary>
        public void LogPayment(
            string studentName,
            string sheetName,
            string courseName,
            string period,
            decimal amountPaid,
            string paymentMode,
            decimal previousBalance,
            decimal newBalance,
            string phoneNumber = "",
            string remarks = "")
        {
            try
            {
                // Load existing logs
                var logs = LoadLogs();

                // Create new log entry
                var newLog = new PaymentLog
                {
                    TransactionId = GenerateTransactionId(),
                    PaymentDate = DateTime.Now,
                    StudentName = studentName,
                    SheetName = sheetName,
                    CourseName = courseName,
                    Period = period,
                    AmountPaid = amountPaid,
                    PaymentMode = paymentMode,
                    PreviousBalance = previousBalance,
                    NewBalance = newBalance,
                    PhoneNumber = phoneNumber,
                    ProcessedBy = Environment.UserName,
                    Remarks = remarks
                };

                // Add to logs
                logs.Add(newLog);

                // Save logs
                SaveLogs(logs);
            }
            catch (Exception ex)
            {
                // Log to a backup file if main file fails
                LogToBackup($"Error logging payment: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all payment logs
        /// </summary>
        public List<PaymentLog> GetAllLogs()
        {
            return LoadLogs();
        }

        /// <summary>
        /// Get payment logs for a specific student
        /// </summary>
        public List<PaymentLog> GetLogsForStudent(string studentName)
        {
            var logs = LoadLogs();
            return logs.Where(l => l.StudentName.Equals(studentName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Get payment logs for a specific sheet/course
        /// </summary>
        public List<PaymentLog> GetLogsForSheet(string sheetName)
        {
            var logs = LoadLogs();
            return logs.Where(l => l.SheetName.Equals(sheetName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Get payment logs within a date range
        /// </summary>
        public List<PaymentLog> GetLogsByDateRange(DateTime startDate, DateTime endDate)
        {
            var logs = LoadLogs();
            return logs.Where(l => l.PaymentDate >= startDate && l.PaymentDate <= endDate).ToList();
        }

        /// <summary>
        /// Get payment logs by payment mode
        /// </summary>
        public List<PaymentLog> GetLogsByPaymentMode(string paymentMode)
        {
            var logs = LoadLogs();
            return logs.Where(l => l.PaymentMode.Equals(paymentMode, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Convert logs to DataTable for reports
        /// </summary>
        public DataTable GetLogsAsDataTable(List<PaymentLog> logs = null)
        {
            if (logs == null)
                logs = LoadLogs();

            var table = new DataTable("PaymentLogs");
            table.Columns.Add("Transaction ID", typeof(string));
            table.Columns.Add("Date & Time", typeof(string));
            table.Columns.Add("Student Name", typeof(string));
            table.Columns.Add("Course", typeof(string));
            table.Columns.Add("Period", typeof(string));
            table.Columns.Add("Amount Paid", typeof(decimal));
            table.Columns.Add("Payment Mode", typeof(string));
            table.Columns.Add("Previous Balance", typeof(decimal));
            table.Columns.Add("New Balance", typeof(decimal));
            table.Columns.Add("Phone", typeof(string));
            table.Columns.Add("Processed By", typeof(string));
            table.Columns.Add("Remarks", typeof(string));

            foreach (var log in logs.OrderByDescending(l => l.PaymentDate))
            {
                table.Rows.Add(
                    log.TransactionId,
                    log.PaymentDate.ToString("dd-MM-yyyy HH:mm:ss"),
                    log.StudentName,
                    log.CourseName,
                    log.Period,
                    log.AmountPaid,
                    log.PaymentMode,
                    log.PreviousBalance,
                    log.NewBalance,
                    log.PhoneNumber,
                    log.ProcessedBy,
                    log.Remarks
                );
            }

            return table;
        }

        /// <summary>
        /// Clear all logs (use with caution)
        /// </summary>
        public void ClearAllLogs()
        {
            SaveLogs(new List<PaymentLog>());
        }

        /// <summary>
        /// Export logs to CSV
        /// </summary>
        public void ExportToCsv(string filePath, List<PaymentLog> logs = null)
        {
            if (logs == null)
                logs = LoadLogs();

            using (var writer = new StreamWriter(filePath))
            {
                // Write header
                writer.WriteLine("Transaction ID,Date & Time,Student Name,Course,Period,Amount Paid,Payment Mode,Previous Balance,New Balance,Phone,Processed By,Remarks");

                // Write data
                foreach (var log in logs.OrderByDescending(l => l.PaymentDate))
                {
                    writer.WriteLine($"\"{log.TransactionId}\",\"{log.PaymentDate:dd-MM-yyyy HH:mm:ss}\",\"{log.StudentName}\",\"{log.CourseName}\",\"{log.Period}\",{log.AmountPaid},\"{log.PaymentMode}\",{log.PreviousBalance},{log.NewBalance},\"{log.PhoneNumber}\",\"{log.ProcessedBy}\",\"{log.Remarks}\"");
                }
            }
        }

        // ===== PRIVATE HELPER METHODS =====

        private List<PaymentLog> LoadLogs()
        {
            try
            {
                if (!File.Exists(_logFilePath))
                    return new List<PaymentLog>();

                var json = File.ReadAllText(_logFilePath);
                return JsonSerializer.Deserialize<List<PaymentLog>>(json) ?? new List<PaymentLog>();
            }
            catch
            {
                return new List<PaymentLog>();
            }
        }

        private void SaveLogs(List<PaymentLog> logs)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(logs, options);
                File.WriteAllText(_logFilePath, json);
            }
            catch (Exception ex)
            {
                LogToBackup($"Error saving logs: {ex.Message}");
            }
        }

        private string GenerateTransactionId()
        {
            return $"TXN{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        private void LogToBackup(string message)
        {
            try
            {
                var backupPath = Path.Combine(_appDataPath, "payment_logs_errors.txt");
                File.AppendAllText(backupPath, $"[{DateTime.Now}] {message}\n");
            }
            catch
            {
                // If backup also fails, silently continue
            }
        }
    }
}