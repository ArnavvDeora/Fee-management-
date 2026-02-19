using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SchoolFeeSystem.Presentation.Services
{
    /// <summary>
    /// Writes every fee payment, fine waiver, or adjustment to a central
    /// CSV-backed payment log so Payment History can:
    ///   • Show all transactions with a dedicated Student Name column.
    ///   • Filter by student name or student ID instantly.
    ///   • Populate the receipt preview from a single row.
    ///
    /// CSV SCHEMA  (columns in order):
    ///   Payment ID | Student Name | Student ID | Guardian | Phone |
    ///   Sheet / Class | Quarter | Payment Date | Amount |
    ///   Payment Mode | Previous Balance | New Balance | Remarks | Recorded By
    ///
    /// BACKWARD COMPATIBILITY:
    ///   Old rows written by earlier versions had no Student Name / Student ID
    ///   columns.  GetPaymentHistory() detects row width and back-fills empty
    ///   strings for those columns so the DataTable always has a uniform schema.
    /// </summary>
    public class PaymentLogService
    {
        // ── Column order ── must stay in sync with _Headers and LogPayment ──
        private static readonly string[] _Headers = new[]
        {
            "Payment ID",
            "Student Name",     // NEW dedicated column — searchable in Payment History
            "Student ID",       // NEW dedicated column — searchable in Payment History
            "Guardian",
            "Phone",
            "Sheet / Class",
            "Quarter",
            "Payment Date",
            "Amount",
            "Payment Mode",
            "Previous Balance",
            "New Balance",
            "Remarks",
            "Recorded By"
        };

        private const int NewColumnCount = 14;

        private readonly string _logFilePath;

        public PaymentLogService(string logFilePath)
        {
            _logFilePath = logFilePath;
            EnsureFileExists();
        }

        // ─────────────────────────────────────────────────────────────────────
        // WRITE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Appends one payment / waiver line to the log CSV.
        /// All caller sites in FeeCollectionViewModel pass studentName and
        /// studentId directly so they appear in their own columns.
        /// </summary>
        public void LogPayment(
            string studentName,
            string studentId,
            string sheetName,
            string courseName,
            string period,
            decimal amountPaid,
            string paymentMode,
            decimal previousBalance,
            decimal newBalance,
            string phoneNumber,
            string guardianName = "",
            string remarks = "")
        {
            string paymentId = Guid.NewGuid().ToString();
            string dateStr = DateTime.Now.ToString("dd/MM/yyyy h:mm:ss tt",
                                                     CultureInfo.InvariantCulture);
            string user = Environment.UserName;

            var fields = new[]
            {
                paymentId,
                Sanitize(studentName),
                Sanitize(studentId),
                Sanitize(guardianName),
                Sanitize(phoneNumber),
                Sanitize(sheetName),
                Sanitize(period),
                dateStr,
                amountPaid.ToString("F2", CultureInfo.InvariantCulture),
                Sanitize(paymentMode),
                previousBalance.ToString("F2", CultureInfo.InvariantCulture),
                newBalance.ToString("F2", CultureInfo.InvariantCulture),
                Sanitize(remarks),
                Sanitize(user)
            };

            File.AppendAllText(_logFilePath,
                string.Join(",", fields.Select(QuoteCsv)) + Environment.NewLine,
                Encoding.UTF8);
        }

        // ─────────────────────────────────────────────────────────────────────
        // READ
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all payment rows as a DataTable with the full schema.
        /// Old rows (fewer columns) are padded with empty strings so the
        /// DataTable is always uniform — no crashes in Payment History.
        /// Rows are returned newest-first.
        /// </summary>
        public DataTable GetPaymentHistory()
        {
            var table = CreateEmptyTable();

            if (!File.Exists(_logFilePath))
                return table;

            var lines = File.ReadAllLines(_logFilePath, Encoding.UTF8);

            // Skip header line(s)
            int start = 0;
            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("Payment ID",
                                         StringComparison.OrdinalIgnoreCase))
                start = 1;

            // Read newest-first
            for (int i = lines.Length - 1; i >= start; i--)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var fields = ParseCsvLine(line);

                // Pad short rows (old schema) to new column count
                while (fields.Length < NewColumnCount)
                    fields = fields.Concat(new[] { "" }).ToArray();

                var row = table.NewRow();
                for (int c = 0; c < NewColumnCount; c++)
                    row[c] = fields[c];

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// Returns all payments for a specific student as a summary DataTable
        /// (groups by Quarter, totals amount paid).
        /// </summary>
        public DataTable GetStudentFinancialSummary(string studentName)
        {
            var all = GetPaymentHistory();
            var summary = new DataTable("Summary");
            summary.Columns.Add("Quarter");
            summary.Columns.Add("Total Paid", typeof(decimal));
            summary.Columns.Add("Transactions", typeof(int));
            summary.Columns.Add("Last Payment");

            var groups = all.AsEnumerable()
                .Where(r => r["Student Name"].ToString()
                              .Contains(studentName, StringComparison.OrdinalIgnoreCase)
                          || r["Student ID"].ToString()
                              .Contains(studentName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r["Quarter"].ToString());

            foreach (var g in groups)
            {
                decimal total = g.Sum(r =>
                    decimal.TryParse(r["Amount"].ToString(), out decimal v) ? v : 0m);
                string last = g.OrderByDescending(r => r["Payment Date"].ToString())
                               .Select(r => r["Payment Date"].ToString())
                               .FirstOrDefault() ?? "";

                var sr = summary.NewRow();
                sr["Quarter"] = g.Key;
                sr["Total Paid"] = total;
                sr["Transactions"] = g.Count();
                sr["Last Payment"] = last;
                summary.Rows.Add(sr);
            }

            return summary;
        }

        // ─────────────────────────────────────────────────────────────────────
        // REPORTS API  (used by ReportsViewModel)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all payment log rows whose Payment Date falls within
        /// [startDate, endDate] (inclusive, date-only comparison).
        /// </summary>
        public List<PaymentLogEntry> GetLogsByDateRange(DateTime startDate, DateTime endDate)
        {
            var all = GetPaymentHistory();
            var result = new List<PaymentLogEntry>();

            foreach (DataRow row in all.Rows)
            {
                if (!DateTime.TryParse(row["Payment Date"]?.ToString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime pd))
                {
                    // Fall back to any-culture parse for older rows
                    if (!DateTime.TryParse(row["Payment Date"]?.ToString(), out pd))
                        continue;
                }

                if (pd.Date < startDate.Date || pd.Date > endDate.Date) continue;

                decimal.TryParse(row["Amount"]?.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal amt);

                result.Add(new PaymentLogEntry
                {
                    PaymentId = row["Payment ID"]?.ToString() ?? "",
                    StudentName = row["Student Name"]?.ToString() ?? "",
                    StudentId = row["Student ID"]?.ToString() ?? "",
                    Guardian = row["Guardian"]?.ToString() ?? "",
                    Phone = row["Phone"]?.ToString() ?? "",
                    SheetClass = row["Sheet / Class"]?.ToString() ?? "",
                    Quarter = row["Quarter"]?.ToString() ?? "",
                    PaymentDate = row["Payment Date"]?.ToString() ?? "",
                    AmountPaid = amt,
                    PaymentMode = row["Payment Mode"]?.ToString() ?? "",
                    PreviousBalance = row["Previous Balance"]?.ToString() ?? "",
                    NewBalance = row["New Balance"]?.ToString() ?? "",
                    Remarks = row["Remarks"]?.ToString() ?? "",
                    RecordedBy = row["Recorded By"]?.ToString() ?? ""
                });
            }

            return result;
        }

        /// <summary>
        /// Converts a list of PaymentLogEntry objects to a DataTable with
        /// the same column layout as GetPaymentHistory(), so the grid and
        /// PDF report service receive a uniform schema.
        /// </summary>
        public DataTable GetLogsAsDataTable(List<PaymentLogEntry> logs)
        {
            var table = CreateEmptyTable();
            foreach (var e in logs)
            {
                var row = table.NewRow();
                row["Payment ID"] = e.PaymentId;
                row["Student Name"] = e.StudentName;
                row["Student ID"] = e.StudentId;
                row["Guardian"] = e.Guardian;
                row["Phone"] = e.Phone;
                row["Sheet / Class"] = e.SheetClass;
                row["Quarter"] = e.Quarter;
                row["Payment Date"] = e.PaymentDate;
                row["Amount"] = e.AmountPaid.ToString("F2",
                                              System.Globalization.CultureInfo.InvariantCulture);
                row["Payment Mode"] = e.PaymentMode;
                row["Previous Balance"] = e.PreviousBalance;
                row["New Balance"] = e.NewBalance;
                row["Remarks"] = e.Remarks;
                row["Recorded By"] = e.RecordedBy;
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>
        /// Writes a list of PaymentLogEntry objects to a new CSV file at
        /// <paramref name="filePath"/> with a header row.
        /// </summary>
        public void ExportToCsv(string filePath, List<PaymentLogEntry> logs)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", _Headers.Select(QuoteCsv)));

            foreach (var e in logs)
            {
                var fields = new[]
                {
                    e.PaymentId,
                    e.StudentName,
                    e.StudentId,
                    e.Guardian,
                    e.Phone,
                    e.SheetClass,
                    e.Quarter,
                    e.PaymentDate,
                    e.AmountPaid.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    e.PaymentMode,
                    e.PreviousBalance,
                    e.NewBalance,
                    e.Remarks,
                    e.RecordedBy
                };
                sb.AppendLine(string.Join(",", fields.Select(QuoteCsv)));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private DataTable CreateEmptyTable()
        {
            var t = new DataTable("PaymentHistory");
            foreach (var h in _Headers)
                t.Columns.Add(h, typeof(string));
            return t;
        }

        private void EnsureFileExists()
        {
            string dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_logFilePath))
                File.WriteAllText(_logFilePath,
                    string.Join(",", _Headers.Select(QuoteCsv)) + Environment.NewLine,
                    Encoding.UTF8);
        }

        private static string Sanitize(string s) =>
            s?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "";

        private static string QuoteCsv(string s)
        {
            if (s == null) return "\"\"";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string[] ParseCsvLine(string line)
        {
            // Simple RFC-4180 parser
            var fields = new System.Collections.Generic.List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DATA TRANSFER OBJECT
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strongly-typed representation of one row in the payment log CSV.
    /// Used by GetLogsByDateRange / GetLogsAsDataTable / ExportToCsv so that
    /// ReportsViewModel can work with real objects (e.g. sum AmountPaid)
    /// without parsing raw DataRow strings.
    /// </summary>
    public class PaymentLogEntry
    {
        public string PaymentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string Guardian { get; set; } = "";
        public string Phone { get; set; } = "";
        public string SheetClass { get; set; } = "";
        public string Quarter { get; set; } = "";
        public string PaymentDate { get; set; } = "";
        public decimal AmountPaid { get; set; }
        public string PaymentMode { get; set; } = "";
        public string PreviousBalance { get; set; } = "";
        public string NewBalance { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string RecordedBy { get; set; } = "";
    }
}