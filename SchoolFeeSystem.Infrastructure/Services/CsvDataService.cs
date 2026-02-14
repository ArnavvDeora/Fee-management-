using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace SchoolFeeSystem.Presentation.Services
{
    public class CsvDataService
    {
        private readonly Dictionary<string, DataSet> _loadedFiles = new();
        private readonly Dictionary<string, string> _filePaths = new();

        // Department mapping based on branch names
        private readonly Dictionary<string, string> _departmentMapping = new()
        {
            { "CS", "Computer Science" },
            { "ME", "Mechanical Engineering" },
            { "EE", "Electrical Engineering" },
            { "CE", "Civil Engineering" },
            { "ECE", "Electronics and Communication" },
            { "IT", "Information Technology" },
            { "CHE", "Chemical Engineering" },
            { "BT", "Biotechnology" }
        };

        // Fee structure configuration
        private readonly Dictionary<string, decimal> _feeStructure = new();

        // Fine calculation parameters
        private const decimal FIRST_MONTH_FINE = 150m;
        private const decimal SECOND_MONTH_DAILY_FINE = 20m;
        private const int SECOND_MONTH_MAX_DAYS = 30;
        private const decimal SECOND_MONTH_MAX_FINE = 600m;
        private const decimal THIRD_MONTH_BASE_FINE = 750m;

        public CsvDataService()
        {
            InitializeFeeStructure();
        }

        private void InitializeFeeStructure()
        {
            // Default fee structure - can be customized
            _feeStructure["Tuition Fee"] = 50000m;
            _feeStructure["Library Fee"] = 5000m;
            _feeStructure["Lab Fee"] = 8000m;
            _feeStructure["Sports Fee"] = 2000m;
            _feeStructure["Development Fee"] = 10000m;
        }

        // ===========================================
        // FILE LOADING WITH DEPARTMENT DETECTION
        // ===========================================

        public void LoadFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var workbook = new XLWorkbook(filePath);
            var dataSet = new DataSet();
            string fileKey = Path.GetFileName(filePath);

            foreach (var worksheet in workbook.Worksheets)
            {
                var table = WorksheetToDataTable(worksheet);

                // Detect and tag department
                string department = DetectDepartment(table);
                table.ExtendedProperties["Department"] = department;
                table.ExtendedProperties["OriginalSheetName"] = worksheet.Name;

                dataSet.Tables.Add(table);
            }

            _loadedFiles[fileKey] = dataSet;
            _filePaths[fileKey] = filePath;
        }

        private string DetectDepartment(DataTable table)
        {
            // Look for department/branch indicators in the data
            foreach (DataRow row in table.Rows)
            {
                foreach (var item in row.ItemArray)
                {
                    if (item == null) continue;
                    string value = item.ToString().ToUpper();

                    // Check for department codes
                    foreach (var dept in _departmentMapping)
                    {
                        if (value.Contains(dept.Key))
                            return dept.Value;
                    }
                }
            }

            // Check column names
            foreach (DataColumn col in table.Columns)
            {
                string colName = col.ColumnName.ToUpper();
                if (colName.Contains("BRANCH") || colName.Contains("DEPARTMENT") || colName.Contains("DEPT"))
                {
                    // Get the most common value in this column
                    var deptValue = table.AsEnumerable()
                        .Select(r => r[col].ToString().ToUpper())
                        .GroupBy(x => x)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault()?.Key;

                    if (!string.IsNullOrEmpty(deptValue))
                    {
                        foreach (var dept in _departmentMapping)
                        {
                            if (deptValue.Contains(dept.Key))
                                return dept.Value;
                        }
                    }
                }
            }

            return "General"; // Default if no department detected
        }

        private DataTable WorksheetToDataTable(IXLWorksheet worksheet)
        {
            var table = new DataTable(worksheet.Name);

            // Find header row (usually row 1 or row with most non-empty cells)
            int headerRow = FindHeaderRow(worksheet);

            // Add columns
            var headerCells = worksheet.Row(headerRow).CellsUsed().ToList();
            foreach (var cell in headerCells)
            {
                string colName = cell.GetString();
                if (string.IsNullOrWhiteSpace(colName))
                    colName = $"Column{cell.Address.ColumnNumber}";

                table.Columns.Add(colName);
            }

            // Add data rows
            foreach (var row in worksheet.RowsUsed().Skip(headerRow))
            {
                if (row.RowNumber() == headerRow) continue;

                var dataRow = table.NewRow();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var cell = row.Cell(i + 1);
                    dataRow[i] = cell.GetString();
                }
                table.Rows.Add(dataRow);
            }

            return table;
        }

        private int FindHeaderRow(IXLWorksheet worksheet)
        {
            int maxCells = 0;
            int headerRow = 1;

            foreach (var row in worksheet.RowsUsed().Take(10)) // Check first 10 rows
            {
                int cellCount = row.CellsUsed().Count();
                if (cellCount > maxCells)
                {
                    maxCells = cellCount;
                    headerRow = row.RowNumber();
                }
            }

            return headerRow;
        }

        // ===========================================
        // DEPARTMENT FILTERING
        // ===========================================

        public List<string> GetDepartments()
        {
            var departments = new HashSet<string>();

            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (table.ExtendedProperties.ContainsKey("Department"))
                    {
                        departments.Add(table.ExtendedProperties["Department"].ToString());
                    }
                }
            }

            return departments.OrderBy(d => d).ToList();
        }

        public List<string> GetSheetsByDepartment(string department)
        {
            var sheets = new List<string>();

            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    if (table.ExtendedProperties["Department"]?.ToString() == department)
                    {
                        string displayName = $"{Path.GetFileNameWithoutExtension(kvp.Key)} - {table.TableName}";
                        sheets.Add(displayName);
                    }
                }
            }

            return sheets;
        }

        public DataTable GetSheetByDepartment(string department, string sheetName)
        {
            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (table.ExtendedProperties["Department"]?.ToString() == department &&
                        table.TableName == sheetName)
                    {
                        return table;
                    }
                }
            }

            return null;
        }

        // ===========================================
        // FINE CALCULATION SYSTEM
        // ===========================================

        public decimal CalculateFine(DateTime feeDate, DateTime currentDate, int monthNumber)
        {
            decimal totalFine = 0m;

            // Calculate days late
            int daysLate = (currentDate - feeDate).Days;

            if (daysLate <= 0)
                return 0m; // No fine if paid on time

            switch (monthNumber)
            {
                case 1: // First month (e.g., August)
                    if (daysLate > 15)
                        totalFine = FIRST_MONTH_FINE;
                    break;

                case 2: // Second month (e.g., September)
                    // Carry forward first month fine if applicable
                    totalFine = FIRST_MONTH_FINE;

                    // Add daily fine for second month
                    int secondMonthDays = Math.Min(daysLate - 15, SECOND_MONTH_MAX_DAYS);
                    decimal secondMonthFine = secondMonthDays * SECOND_MONTH_DAILY_FINE;
                    totalFine += Math.Min(secondMonthFine, SECOND_MONTH_MAX_FINE);
                    break;

                case 3: // Third month onwards
                default:
                    // Accumulated fines from previous months
                    totalFine = FIRST_MONTH_FINE + SECOND_MONTH_MAX_FINE + THIRD_MONTH_BASE_FINE;

                    // Additional fine for months beyond third
                    if (monthNumber > 3)
                    {
                        totalFine += (monthNumber - 3) * THIRD_MONTH_BASE_FINE;
                    }
                    break;
            }

            return totalFine;
        }

        public void RecalculateRowFees(string sheetName, DataRow row)
        {
            // Check if fee columns exist
            if (!row.Table.Columns.Contains("Total Fee") ||
                !row.Table.Columns.Contains("Fine Amount"))
                return;

            try
            {
                // Get payment date if available
                DateTime? paymentDate = null;
                if (row.Table.Columns.Contains("Payment Date") &&
                    !string.IsNullOrEmpty(row["Payment Date"]?.ToString()))
                {
                    paymentDate = DateTime.Parse(row["Payment Date"].ToString());
                }

                // Get due date if available
                DateTime? dueDate = null;
                if (row.Table.Columns.Contains("Due Date") &&
                    !string.IsNullOrEmpty(row["Due Date"]?.ToString()))
                {
                    dueDate = DateTime.Parse(row["Due Date"].ToString());
                }

                // Calculate fine if payment is late
                if (paymentDate.HasValue && dueDate.HasValue && paymentDate > dueDate)
                {
                    int monthNumber = GetMonthNumber(dueDate.Value);
                    decimal calculatedFine = CalculateFine(dueDate.Value, paymentDate.Value, monthNumber);
                    row["Fine Amount"] = calculatedFine;
                }
                else if (!paymentDate.HasValue && dueDate.HasValue && DateTime.Now > dueDate)
                {
                    // Payment not yet made, calculate current fine
                    int monthNumber = GetMonthNumber(dueDate.Value);
                    decimal calculatedFine = CalculateFine(dueDate.Value, DateTime.Now, monthNumber);
                    row["Fine Amount"] = calculatedFine;
                }
                else
                {
                    row["Fine Amount"] = 0m;
                }

                // Recalculate total amount
                decimal baseFee = 0m;
                if (row.Table.Columns.Contains("Base Fee") &&
                    !string.IsNullOrEmpty(row["Base Fee"]?.ToString()))
                {
                    baseFee = decimal.Parse(row["Base Fee"].ToString());
                }

                decimal fineAmount = decimal.Parse(row["Fine Amount"].ToString());
                row["Total Fee"] = baseFee + fineAmount;
            }
            catch (Exception ex)
            {
                // Log error but don't throw to prevent UI crashes
                Console.WriteLine($"Error recalculating fees: {ex.Message}");
            }
        }

        private int GetMonthNumber(DateTime dueDate)
        {
            // Calculate which month this is in the academic year
            // Assuming academic year starts in August
            int academicYearStart = 8; // August
            int currentMonth = DateTime.Now.Month;
            int dueMonth = dueDate.Month;

            if (currentMonth >= academicYearStart)
                return currentMonth - academicYearStart + 1;
            else
                return 12 - academicYearStart + currentMonth + 1;
        }

        public DataTable GetFineReport()
        {
            var fineReport = new DataTable("Fine Report");
            fineReport.Columns.Add("Student ID");
            fineReport.Columns.Add("Student Name");
            fineReport.Columns.Add("Department");
            fineReport.Columns.Add("Class");
            fineReport.Columns.Add("Due Date");
            fineReport.Columns.Add("Payment Date");
            fineReport.Columns.Add("Days Late");
            fineReport.Columns.Add("Fine Amount", typeof(decimal));
            fineReport.Columns.Add("Status");

            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    string department = table.ExtendedProperties["Department"]?.ToString() ?? "Unknown";

                    foreach (DataRow row in table.Rows)
                    {
                        // Check if this row has a fine
                        if (table.Columns.Contains("Fine Amount") &&
                            row["Fine Amount"] != DBNull.Value &&
                            decimal.Parse(row["Fine Amount"].ToString()) > 0)
                        {
                            var reportRow = fineReport.NewRow();

                            reportRow["Student ID"] = GetValueOrDefault(row, "Student ID", "Roll No", "ID");
                            reportRow["Student Name"] = GetValueOrDefault(row, "Student Name", "Name");
                            reportRow["Department"] = department;
                            reportRow["Class"] = GetValueOrDefault(row, "Class", "Section");
                            reportRow["Due Date"] = GetValueOrDefault(row, "Due Date");
                            reportRow["Payment Date"] = GetValueOrDefault(row, "Payment Date");

                            // Calculate days late
                            if (DateTime.TryParse(reportRow["Due Date"].ToString(), out DateTime dueDate))
                            {
                                DateTime compareDate = DateTime.TryParse(reportRow["Payment Date"].ToString(), out DateTime payDate)
                                    ? payDate
                                    : DateTime.Now;
                                reportRow["Days Late"] = Math.Max(0, (compareDate - dueDate).Days);
                            }

                            reportRow["Fine Amount"] = row["Fine Amount"];
                            reportRow["Status"] = string.IsNullOrEmpty(GetValueOrDefault(row, "Payment Date"))
                                ? "Pending"
                                : "Paid";

                            fineReport.Rows.Add(reportRow);
                        }
                    }
                }
            }

            return fineReport;
        }

        // ===========================================
        // PAYMENT HISTORY TRACKING
        // ===========================================

        public void RecordPayment(string studentId, decimal amount, string paymentType, string remarks = "")
        {
            var paymentHistory = GetOrCreatePaymentHistoryTable();

            var newPayment = paymentHistory.NewRow();
            newPayment["Payment ID"] = Guid.NewGuid().ToString();
            newPayment["Student ID"] = studentId;
            newPayment["Payment Date"] = DateTime.Now;
            newPayment["Amount"] = amount;
            newPayment["Payment Type"] = paymentType; // "Fee", "Fine", "Other"
            newPayment["Remarks"] = remarks;
            newPayment["Recorded By"] = Environment.UserName;

            paymentHistory.Rows.Add(newPayment);
        }

        private DataTable GetOrCreatePaymentHistoryTable()
        {
            const string historyKey = "_PaymentHistory";

            if (!_loadedFiles.ContainsKey(historyKey))
            {
                var dataSet = new DataSet();
                var historyTable = new DataTable("Payment History");

                historyTable.Columns.Add("Payment ID");
                historyTable.Columns.Add("Student ID");
                historyTable.Columns.Add("Payment Date", typeof(DateTime));
                historyTable.Columns.Add("Amount", typeof(decimal));
                historyTable.Columns.Add("Payment Type");
                historyTable.Columns.Add("Remarks");
                historyTable.Columns.Add("Recorded By");

                dataSet.Tables.Add(historyTable);
                _loadedFiles[historyKey] = dataSet;
            }

            return _loadedFiles[historyKey].Tables[0];
        }

        public DataTable GetPaymentHistory(string studentId = null)
        {
            var historyTable = GetOrCreatePaymentHistoryTable();

            if (string.IsNullOrEmpty(studentId))
                return historyTable.Copy();

            var filtered = historyTable.Clone();
            foreach (DataRow row in historyTable.Rows)
            {
                if (row["Student ID"].ToString() == studentId)
                {
                    filtered.ImportRow(row);
                }
            }

            return filtered;
        }

        public DataTable GetStudentFinancialSummary(string studentId)
        {
            var summary = new DataTable("Financial Summary");
            summary.Columns.Add("Category");
            summary.Columns.Add("Amount", typeof(decimal));
            summary.Columns.Add("Status");

            decimal totalFees = 0m;
            decimal totalFines = 0m;
            decimal totalPaid = 0m;

            // Calculate from payment history
            var history = GetPaymentHistory(studentId);
            foreach (DataRow row in history.Rows)
            {
                decimal amount = decimal.Parse(row["Amount"].ToString());
                string type = row["Payment Type"].ToString();

                if (type == "Fee")
                    totalFees += amount;
                else if (type == "Fine")
                    totalFines += amount;

                totalPaid += amount;
            }

            // Get pending fines from current data
            decimal pendingFines = 0m;
            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    foreach (DataRow row in table.Rows)
                    {
                        if (GetValueOrDefault(row, "Student ID", "Roll No", "ID") == studentId)
                        {
                            if (table.Columns.Contains("Fine Amount") && row["Fine Amount"] != DBNull.Value)
                            {
                                string paymentStatus = GetValueOrDefault(row, "Payment Date");
                                if (string.IsNullOrEmpty(paymentStatus))
                                {
                                    pendingFines += decimal.Parse(row["Fine Amount"].ToString());
                                }
                            }
                        }
                    }
                }
            }

            // Add summary rows
            summary.Rows.Add("Total Fees Paid", totalFees, "Completed");
            summary.Rows.Add("Total Fines Paid", totalFines, "Completed");
            summary.Rows.Add("Pending Fines", pendingFines, pendingFines > 0 ? "Pending" : "Clear");
            summary.Rows.Add("Total Amount Paid", totalPaid, "Completed");
            summary.Rows.Add("Outstanding Amount", pendingFines, pendingFines > 0 ? "Due" : "Clear");

            return summary;
        }

        // ===========================================
        // UTILITY METHODS
        // ===========================================

        private string GetValueOrDefault(DataRow row, params string[] columnNames)
        {
            foreach (var colName in columnNames)
            {
                if (row.Table.Columns.Contains(colName) && row[colName] != DBNull.Value)
                {
                    return row[colName].ToString();
                }
            }
            return string.Empty;
        }

        public List<string> GetLoadedFiles()
        {
            return _filePaths.Values.ToList();
        }

        public void RemoveFile(string filePath)
        {
            string fileKey = Path.GetFileName(filePath);
            _loadedFiles.Remove(fileKey);
            _filePaths.Remove(fileKey);
        }

        public List<string> GetSheetDisplayNames()
        {
            var names = new List<string>();
            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    string displayName = $"{Path.GetFileNameWithoutExtension(kvp.Key)} - {table.TableName}";
                    names.Add(displayName);
                }
            }
            return names;
        }

        public string GetSheetNameFromDisplay(string displayName)
        {
            // Extract sheet name from "FileName - SheetName" format
            var parts = displayName.Split(new[] { " - " }, StringSplitOptions.None);
            return parts.Length > 1 ? parts[1] : displayName;
        }

        // Used by ClassViewModel.RemoveCourse() to find which .xlsx file
        // owns a given sheet so it can pass the full path to RemoveFile().
        public string GetFilePathForSheet(string sheetName)
        {
            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    if (table.TableName == sheetName)
                    {
                        return _filePaths.TryGetValue(kvp.Key, out var fullPath)
                            ? fullPath
                            : null;
                    }
                }
            }
            return null;
        }

        // Returns all raw sheet/table names across every loaded file.
        public List<string> GetSheetNames()
        {
            var names = new List<string>();
            foreach (var dataSet in _loadedFiles.Values)
                foreach (DataTable table in dataSet.Tables)
                    names.Add(table.TableName);
            return names;
        }

        public DataTable GetSheet(string sheetName)
        {
            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (table.TableName == sheetName)
                        return table;
                }
            }
            return null;
        }

        public void SaveFile()
        {
            foreach (var kvp in _filePaths)
            {
                if (!_loadedFiles.ContainsKey(kvp.Key))
                    continue;

                var filePath = kvp.Value;
                var dataSet = _loadedFiles[kvp.Key];

                using var workbook = new XLWorkbook();

                foreach (DataTable table in dataSet.Tables)
                {
                    var worksheet = workbook.AddWorksheet(table.TableName);

                    // Add headers
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        worksheet.Cell(1, i + 1).Value = table.Columns[i].ColumnName;
                        worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    }

                    // Add data
                    for (int row = 0; row < table.Rows.Count; row++)
                    {
                        for (int col = 0; col < table.Columns.Count; col++)
                        {
                            worksheet.Cell(row + 2, col + 1).Value = table.Rows[row][col]?.ToString();
                        }
                    }

                    worksheet.Columns().AdjustToContents();
                }

                workbook.SaveAs(filePath);
            }

            // Save payment history separately
            const string historyKey = "_PaymentHistory";
            if (_loadedFiles.ContainsKey(historyKey))
            {
                string historyPath = Path.Combine(
                    Path.GetDirectoryName(_filePaths.Values.FirstOrDefault() ?? ""),
                    "PaymentHistory.xlsx");

                using var workbook = new XLWorkbook();
                var table = _loadedFiles[historyKey].Tables[0];
                var worksheet = workbook.AddWorksheet("Payment History");

                // Add headers
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    worksheet.Cell(1, i + 1).Value = table.Columns[i].ColumnName;
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                }

                // Add data
                for (int row = 0; row < table.Rows.Count; row++)
                {
                    for (int col = 0; col < table.Columns.Count; col++)
                    {
                        worksheet.Cell(row + 2, col + 1).Value = table.Rows[row][col]?.ToString();
                    }
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(historyPath);
            }
        }


        // ===========================================
        // SHEET NOTE AND INCREMENT MANAGEMENT
        // ===========================================

        public SheetNoteInfo GetSheetNote(string sheetName)
        {
            var table = GetSheet(sheetName);
            if (table == null) return null;

            // Look for a note in ExtendedProperties or in a special note row/column
            if (table.ExtendedProperties.ContainsKey("AutoIncrementNote"))
            {
                return table.ExtendedProperties["AutoIncrementNote"] as SheetNoteInfo;
            }

            // Try to parse from a Note column or first row
            var noteCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("note"));

            if (noteCol != null && table.Rows.Count > 0)
            {
                string noteText = table.Rows[0][noteCol]?.ToString() ?? "";
                return ParseNoteInfo(noteText);
            }

            return null;
        }

        private SheetNoteInfo ParseNoteInfo(string noteText)
        {
            if (string.IsNullOrWhiteSpace(noteText)) return null;

            // Expected format: "Note: Increment ₹500 after 15-04-2025"
            var match = System.Text.RegularExpressions.Regex.Match(
                noteText,
                @"Increment\s*₹?(\d+(?:\.\d+)?)\s*after\s*(\d{1,2}[-/]\d{1,2}[-/]\d{2,4})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                decimal amount = decimal.Parse(match.Groups[1].Value);
                DateTime date = DateTime.ParseExact(
                    match.Groups[2].Value,
                    new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy" },
                    null,
                    System.Globalization.DateTimeStyles.None
                );

                return new SheetNoteInfo
                {
                    IncrementAmount = amount,
                    IncrementDate = date,
                    OriginalNoteText = noteText
                };
            }

            return null;
        }

        public void UpdateExtensionDate(string sheetName, DateTime newDate)
        {
            var table = GetSheet(sheetName);
            if (table == null) return;

            var noteInfo = GetSheetNote(sheetName);
            if (noteInfo != null)
            {
                noteInfo.IncrementDate = newDate;
                table.ExtendedProperties["AutoIncrementNote"] = noteInfo;

                // Update in the table if it exists
                var noteCol = table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.ToLower().Contains("note"));

                if (noteCol != null && table.Rows.Count > 0)
                {
                    table.Rows[0][noteCol] = $"Note: Increment ₹{noteInfo.IncrementAmount} after {newDate:dd-MM-yyyy}";
                }
            }
        }

        public void ManuallyApplyIncrement(string sheetName)
        {
            var table = GetSheet(sheetName);
            if (table == null) return;

            var noteInfo = GetSheetNote(sheetName);
            if (noteInfo == null) return;

            // Find pending/quarterly fees column
            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending"));

            if (pendingCol != null)
            {
                foreach (DataRow row in table.Rows)
                {
                    string rawValue = row[pendingCol]?.ToString()?.Trim();
                    if (decimal.TryParse(rawValue?.Replace("₹", "").Replace(",", ""), out decimal current) && current > 0)
                    {
                        row[pendingCol] = (current + noteInfo.IncrementAmount).ToString("F2");
                    }
                }
            }
        }

        // ===========================================
        // METADATA MANAGEMENT
        // ===========================================

        public SheetMetadata GetSheetMetadata(string sheetName)
        {
            var table = GetSheet(sheetName);
            if (table == null) return null;

            // Check if metadata is stored in ExtendedProperties
            if (table.ExtendedProperties.ContainsKey("SheetMetadata"))
            {
                return table.ExtendedProperties["SheetMetadata"] as SheetMetadata;
            }

            // Try to infer from sheet name or table properties
            return new SheetMetadata
            {
                InstituteName = "School Fee System",
                Period = ExtractPeriodFromSheetName(sheetName),
                CourseInfo = sheetName
            };
        }

        private string ExtractPeriodFromSheetName(string sheetName)
        {
            // Try to extract date/period info from sheet name
            var match = System.Text.RegularExpressions.Regex.Match(
                sheetName,
                @"(\d{4}|\w+\s+\d{4}|Q[1-4]\s+\d{4})"
            );

            return match.Success ? match.Value : DateTime.Now.Year.ToString();
        }

        // ===========================================
        // PAYMENT RECORDING (OVERLOADED)
        // ===========================================

        public void RecordPayment(string sheetName, DataRow studentRow, decimal amount, string paymentMode, DateTime paymentDate)
        {
            var historyTable = GetOrCreatePaymentHistoryTable();

            // Get student ID from the row
            string studentId = GetValueOrDefault(studentRow, "Student ID", "Roll No", "ID", "Reg No");

            var newPayment = historyTable.NewRow();
            newPayment["Payment ID"] = Guid.NewGuid().ToString();
            newPayment["Student ID"] = studentId;
            newPayment["Payment Date"] = paymentDate;
            newPayment["Amount"] = amount;
            newPayment["Payment Type"] = "Fee";
            newPayment["Remarks"] = $"Payment for {sheetName} via {paymentMode}";
            newPayment["Recorded By"] = Environment.UserName;

            historyTable.Rows.Add(newPayment);
        }

        // ===========================================
        // FILTERED VIEWS
        // ===========================================

        public DataView GetPendingFeesView(string sheetName)
        {
            var table = GetSheet(sheetName);
            if (table == null) return null;

            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                   c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("balance"));

            if (pendingCol == null)
                return table.DefaultView;

            var filteredTable = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                string rawValue = row[pendingCol]?.ToString()?.Trim();
                if (decimal.TryParse(rawValue?.Replace("₹", "").Replace(",", ""), out decimal pending) && pending > 0)
                {
                    filteredTable.ImportRow(row);
                }
            }

            return filteredTable.DefaultView;
        }

        public DataView GetScholarshipView(string sheetName)
        {
            var table = GetSheet(sheetName);
            if (table == null) return null;

            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            if (scholarshipCol == null)
                return table.DefaultView;

            var filteredTable = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                string rawValue = row[scholarshipCol]?.ToString()?.Trim();
                if (decimal.TryParse(rawValue?.Replace("%", "").Replace("₹", "").Replace(",", ""), out decimal scholarship) && scholarship > 0)
                {
                    filteredTable.ImportRow(row);
                }
            }

            return filteredTable.DefaultView;
        }

        // ===========================================
        // SCHOLARSHIP MANAGEMENT
        // ===========================================

        public void ApplyScholarship(string sheetName, DataRow studentRow, decimal scholarshipPercentage)
        {
            var table = GetSheet(sheetName);
            if (table == null) return;

            // Find scholarship column
            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            if (scholarshipCol == null)
            {
                // Add scholarship column if it doesn't exist
                scholarshipCol = table.Columns.Add("Scholarship %", typeof(decimal));
            }

            // Find the row in the table
            DataRow targetRow = null;
            foreach (DataRow row in table.Rows)
            {
                bool match = true;
                for (int i = 0; i < Math.Min(row.ItemArray.Length, studentRow.ItemArray.Length); i++)
                {
                    if (!row[i].Equals(studentRow[i]))
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

            if (targetRow != null)
            {
                targetRow[scholarshipCol] = scholarshipPercentage;

                // Recalculate fees if there's a quarterly fees column
                var quarterlyCol = table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.ToLower().Contains("quarterly"));

                var totalCol = table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total") &&
                                       !c.ColumnName.ToLower().Contains("paid"));

                if (quarterlyCol != null && totalCol != null)
                {
                    string rawQuarterly = targetRow[quarterlyCol]?.ToString()?.Trim();
                    if (decimal.TryParse(rawQuarterly?.Replace("₹", "").Replace(",", ""), out decimal quarterly))
                    {
                        decimal discount = quarterly * (scholarshipPercentage / 100);
                        decimal adjustedQuarterly = quarterly - discount;

                        // Update total (assuming total = previous + adjusted quarterly)
                        var previousCol = table.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                                               c.ColumnName.ToLower().Contains("pending"));

                        decimal previous = 0;
                        if (previousCol != null)
                        {
                            string rawPrevious = targetRow[previousCol]?.ToString()?.Trim();
                            decimal.TryParse(rawPrevious?.Replace("₹", "").Replace(",", ""), out previous);
                        }

                        targetRow[totalCol] = (previous + adjustedQuarterly).ToString("F2");
                    }
                }
            }
        }
        // ========================================================================
        // ADD THESE METHODS TO YOUR EXISTING CsvDataService.cs FILE
        // These methods support the enhanced ClassView with department-based organization
        // ========================================================================

        // Add this inside the CsvDataService class:

        // ===========================================
        // DEPARTMENT & YEAR FILTERING
        // ===========================================

        /// <summary>
        /// Get a sheet by department code, year, and quarter
        /// Example: GetSheetByFilter("ME", 1, "Aug-Oct") returns "Mechanical-1st-AugOct" sheet
        /// </summary>
        public DataTable GetSheetByFilter(string departmentCode, int year, string quarter)
        {
            // Build possible sheet name patterns
            string quarterCode = quarter.Replace("-", "");

            // Try different naming patterns that might exist in uploaded files
            string[] possibleNames = new[]
            {
        $"{departmentCode}-{year}-{quarterCode}",
        $"{departmentCode}-Year{year}-{quarterCode}",
        $"{departmentCode} {year} {quarter}",
        $"{departmentCode} Year {year} {quarter}",
        $"{departmentCode} - {year}st Year - {quarter}",
        $"{departmentCode} - {year}nd Year - {quarter}",
        $"{departmentCode} - {year}rd Year - {quarter}",
        $"{departmentCode} - {year}th Year - {quarter}",
    };

            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    string tableName = table.TableName;
                    string originalName = table.ExtendedProperties["OriginalSheetName"]?.ToString() ?? "";

                    // Check if any pattern matches
                    foreach (var pattern in possibleNames)
                    {
                        if (tableName.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                            originalName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            return table;
                        }
                    }

                    // Also check metadata if it exists
                    if (table.ExtendedProperties["Department"]?.ToString() == departmentCode)
                    {
                        // Check if year and quarter match in the data
                        if (TableMatchesYearAndQuarter(table, year, quarter))
                        {
                            return table;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a table contains data for a specific year and quarter
        /// </summary>
        private bool TableMatchesYearAndQuarter(DataTable table, int year, string quarter)
        {
            // Look for year indicators in table headers or first few rows
            foreach (DataRow row in table.Rows.Cast<DataRow>().Take(5))
            {
                foreach (var item in row.ItemArray)
                {
                    if (item == null) continue;
                    string value = item.ToString();

                    // Check for year patterns like "1st Year", "2nd Year", "Year 1", etc.
                    if (value.Contains($"{year}st", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"{year}nd", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"{year}rd", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"{year}th", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($"Year {year}", StringComparison.OrdinalIgnoreCase))
                    {
                        // Also check for quarter
                        if (value.Contains(quarter, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get available academic years for a department (for pass-outs)
        /// </summary>
        public List<int> GetAvailableAcademicYears(string departmentCode)
        {
            var years = new HashSet<int>();

            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    if (table.ExtendedProperties["Department"]?.ToString() == departmentCode)
                    {
                        // Extract year from sheet name or data
                        int year = ExtractYearFromTable(table);
                        if (year > 2000)
                        {
                            years.Add(year);
                        }
                    }
                }
            }

            return years.OrderByDescending(y => y).ToList();
        }

        private int ExtractYearFromTable(DataTable table)
        {
            // Try to extract year from sheet name or data
            string tableName = table.TableName;

            // Look for 4-digit year patterns
            var match = System.Text.RegularExpressions.Regex.Match(tableName, @"\b(20\d{2})\b");
            if (match.Success && int.TryParse(match.Value, out int year))
            {
                return year;
            }

            // Check first few rows for year information
            foreach (DataRow row in table.Rows.Cast<DataRow>().Take(5))
            {
                foreach (var item in row.ItemArray)
                {
                    if (item == null) continue;
                    string value = item.ToString();

                    match = System.Text.RegularExpressions.Regex.Match(value, @"\b(20\d{2})\b");
                    if (match.Success && int.TryParse(match.Value, out year))
                    {
                        return year;
                    }
                }
            }

            return DateTime.Now.Year;
        }

        // ===========================================
        // YEAR PROGRESSION
        // ===========================================

        /// <summary>
        /// Promote all students from one year to the next
        /// If isLastYear = true, moves students to pass-outs
        /// </summary>
        public void PromoteStudentsToNextYear(string departmentCode, int currentYear, bool isLastYear)
        {
            // Find all sheets for this department and year
            var sheetsToPromote = new List<DataTable>();

            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    if (table.ExtendedProperties["Department"]?.ToString() == departmentCode)
                    {
                        if (TableMatchesYear(table, currentYear))
                        {
                            sheetsToPromote.Add(table);
                        }
                    }
                }
            }

            if (sheetsToPromote.Count == 0)
            {
                throw new InvalidOperationException($"No data found for {departmentCode} Year {currentYear}");
            }

            // Create new sheets for promoted students
            foreach (var sourceTable in sheetsToPromote)
            {
                string newSheetName;

                if (isLastYear)
                {
                    // Move to pass-outs
                    newSheetName = $"PASSOUT-{departmentCode}-{DateTime.Now.Year}";
                }
                else
                {
                    // Move to next year
                    string oldName = sourceTable.TableName;
                    newSheetName = oldName.Replace($"Year{currentYear}", $"Year{currentYear + 1}")
                                          .Replace($"{currentYear}st", $"{currentYear + 1}st")
                                          .Replace($"{currentYear}nd", $"{currentYear + 1}nd")
                                          .Replace($"{currentYear}rd", $"{currentYear + 1}rd")
                                          .Replace($"{currentYear}th", $"{currentYear + 1}th");
                }

                // Clone the table structure and data
                var newTable = sourceTable.Copy();
                newTable.TableName = newSheetName;

                // Update metadata
                newTable.ExtendedProperties["Department"] = isLastYear ? "PASSOUT" : departmentCode;
                newTable.ExtendedProperties["Year"] = isLastYear ? "Graduate" : (currentYear + 1).ToString();
                newTable.ExtendedProperties["OriginalSheetName"] = newSheetName;

                // Reset fee-related columns for new academic year
                ResetFeesForNewYear(newTable);

                // Add to appropriate file
                string fileKey = isLastYear ? "PassOuts.xlsx" : $"{departmentCode}_Year{currentYear + 1}.xlsx";

                if (!_loadedFiles.ContainsKey(fileKey))
                {
                    _loadedFiles[fileKey] = new DataSet();
                }

                _loadedFiles[fileKey].Tables.Add(newTable);
            }

            // Optionally archive or remove old year data
            // (You might want to keep it for historical purposes)
        }

        private bool TableMatchesYear(DataTable table, int year)
        {
            string tableName = table.TableName.ToLower();

            return tableName.Contains($"{year}st") ||
                   tableName.Contains($"{year}nd") ||
                   tableName.Contains($"{year}rd") ||
                   tableName.Contains($"{year}th") ||
                   tableName.Contains($"year{year}") ||
                   tableName.Contains($"year {year}");
        }

        private void ResetFeesForNewYear(DataTable table)
        {
            // Find fee-related columns
            var feeColumns = table.Columns.Cast<DataColumn>()
                .Where(c => c.ColumnName.ToLower().Contains("paid") ||
                           c.ColumnName.ToLower().Contains("total") ||
                           c.ColumnName.ToLower().Contains("balance"))
                .ToList();

            // Reset all fee columns to 0 or empty
            foreach (DataRow row in table.Rows)
            {
                foreach (var col in feeColumns)
                {
                    if (col.DataType == typeof(decimal) || col.DataType == typeof(double))
                    {
                        row[col] = 0;
                    }
                    else
                    {
                        row[col] = "₹0.00";
                    }
                }
            }

            // Move "Previous Pending" to new column for tracking
            var previousCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous"));

            if (previousCol != null && !table.Columns.Contains("Carried Forward"))
            {
                var carriedForwardCol = table.Columns.Add("Carried Forward", typeof(string));

                foreach (DataRow row in table.Rows)
                {
                    row[carriedForwardCol] = row[previousCol];
                    row[previousCol] = "₹0.00";
                }
            }
        }

        // ===========================================
        // COURSE & QUARTER DETECTION FROM EXCEL
        // ===========================================

        /// <summary>
        /// Enhanced file loading with better course/year/quarter detection
        /// </summary>
        public void LoadFileEnhanced(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var workbook = new XLWorkbook(filePath);
            var dataSet = new DataSet();
            string fileKey = Path.GetFileName(filePath);

            foreach (var worksheet in workbook.Worksheets)
            {
                var table = WorksheetToDataTable(worksheet);

                // Extract metadata from sheet header (rows 1-3)
                var metadata = ExtractMetadataFromSheet(worksheet);

                // Apply metadata to table
                table.ExtendedProperties["Department"] = metadata.DepartmentCode;
                table.ExtendedProperties["Year"] = metadata.Year;
                table.ExtendedProperties["Quarter"] = metadata.Quarter;
                table.ExtendedProperties["InstituteName"] = metadata.InstituteName;
                table.ExtendedProperties["Period"] = metadata.Period;
                table.ExtendedProperties["CourseInfo"] = metadata.CourseInfo;
                table.ExtendedProperties["OriginalSheetName"] = worksheet.Name;

                dataSet.Tables.Add(table);
            }

            _loadedFiles[fileKey] = dataSet;
            _filePaths[fileKey] = filePath;
        }

        private SheetMetadataExtended ExtractMetadataFromSheet(IXLWorksheet worksheet)
        {
            var metadata = new SheetMetadataExtended();

            // Read first 3 rows to extract metadata
            // Example format:
            // Row 1: "CENTRAL INSTITUTE OF HAND TOOLS - SRI LANKA"
            // Row 2: "Sub-Deposition of fee for the period: FEB 2026 to APRIL 2026"
            // Row 3: "Diploma - Mechanical Engineering (Tool and Die) - 3RD Year - 18th Batch"

            if (worksheet.RowsUsed().Count() >= 3)
            {
                string row1 = worksheet.Row(1).CellsUsed().FirstOrDefault()?.GetString() ?? "";
                string row2 = worksheet.Row(2).CellsUsed().FirstOrDefault()?.GetString() ?? "";
                string row3 = worksheet.Row(3).CellsUsed().FirstOrDefault()?.GetString() ?? "";

                // Extract institute name
                metadata.InstituteName = row1.Trim();

                // Extract period from row 2
                var periodMatch = System.Text.RegularExpressions.Regex.Match(row2,
                    @"(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}\s+to\s+(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (periodMatch.Success)
                {
                    metadata.Period = periodMatch.Value;
                    metadata.Quarter = DeterminQuarter(periodMatch.Value);
                }

                // Extract course info from row 3
                metadata.CourseInfo = row3.Trim();

                // Extract department
                if (row3.Contains("Mechanical", StringComparison.OrdinalIgnoreCase))
                    metadata.DepartmentCode = "ME";
                else if (row3.Contains("Mechatronics", StringComparison.OrdinalIgnoreCase))
                    metadata.DepartmentCode = "MECHATRONICS";
                else if (row3.Contains("Electrical", StringComparison.OrdinalIgnoreCase))
                    metadata.DepartmentCode = "EE";
                else if (row3.Contains("Computer", StringComparison.OrdinalIgnoreCase))
                    metadata.DepartmentCode = "CSE";
                else
                    metadata.DepartmentCode = "MISC";

                // Extract year
                var yearMatch = System.Text.RegularExpressions.Regex.Match(row3, @"(\d+)(st|nd|rd|th)\s+Year",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (yearMatch.Success)
                {
                    metadata.Year = yearMatch.Groups[1].Value;
                }
            }

            return metadata;
        }

        private string DeterminQuarter(string period)
        {
            if (period.Contains("AUG", StringComparison.OrdinalIgnoreCase) ||
                period.Contains("SEP", StringComparison.OrdinalIgnoreCase) ||
                period.Contains("OCT", StringComparison.OrdinalIgnoreCase))
            {
                return "Aug-Oct";
            }
            else if (period.Contains("NOV", StringComparison.OrdinalIgnoreCase) ||
                     period.Contains("DEC", StringComparison.OrdinalIgnoreCase) ||
                     period.Contains("JAN", StringComparison.OrdinalIgnoreCase))
            {
                return "Nov-Jan";
            }
            else if (period.Contains("FEB", StringComparison.OrdinalIgnoreCase) ||
                     period.Contains("MAR", StringComparison.OrdinalIgnoreCase) ||
                     period.Contains("APR", StringComparison.OrdinalIgnoreCase))
            {
                return "Feb-Apr";
            }

            return "Unknown";
        }

        // ===========================================
        // HELPER CLASSES
        // ===========================================

        private class SheetMetadataExtended
        {
            public string InstituteName { get; set; } = "";
            public string Period { get; set; } = "";
            public string CourseInfo { get; set; } = "";
            public string DepartmentCode { get; set; } = "";
            public string Year { get; set; } = "";
            public string Quarter { get; set; } = "";
        }

        // ===========================================
        // HELPER CLASSES
        // ===========================================

        public class SheetNoteInfo
        {
            public decimal IncrementAmount { get; set; }
            public DateTime IncrementDate { get; set; }
            public string OriginalNoteText { get; set; }
        }

        // SheetMetadata class definition
        public class SheetMetadata
        {
            public string InstituteName { get; set; }
            public string Period { get; set; }
            public string CourseInfo { get; set; }
        }
    }
}