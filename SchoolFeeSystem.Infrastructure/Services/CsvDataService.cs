using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolFeeSystem.Presentation.Services
{
    public class CsvDataService
    {
        private readonly Dictionary<string, DataTable> _sheets = new();
        private readonly Dictionary<string, SheetMetadata> _metadata = new();
        private readonly Dictionary<string, SheetNoteInfo> _sheetNotes = new();
        private readonly List<string> _loadedFiles = new();
        private readonly string _appDataPath;
        private readonly string _filesListPath;
        private string _currentFilePath;

        public class SheetMetadata
        {
            public string InstituteName { get; set; }
            public string Period { get; set; }
            public string CourseInfo { get; set; }
            public string DisplayName { get; set; }
            public bool IsEmpty { get; set; } // Track if sheet has actual data
        }

        public class SheetNoteInfo
        {
            public decimal IncrementAmount { get; set; }
            public DateTime IncrementDate { get; set; }
            public string RawNote { get; set; }
        }

        public CsvDataService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem"
            );

            if (!Directory.Exists(_appDataPath))
                Directory.CreateDirectory(_appDataPath);

            _filesListPath = Path.Combine(_appDataPath, "loaded_files.json");
            LoadFilesList();
        }

        private void LoadFilesList()
        {
            if (File.Exists(_filesListPath))
            {
                try
                {
                    var json = File.ReadAllText(_filesListPath);
                    var files = JsonSerializer.Deserialize<List<string>>(json);

                    if (files != null)
                    {
                        foreach (var file in files)
                        {
                            if (File.Exists(file))
                            {
                                LoadFile(file);
                            }
                        }
                    }
                }
                catch
                {
                    _loadedFiles.Clear();
                }
            }
        }

        private void SaveFilesList()
        {
            try
            {
                var json = JsonSerializer.Serialize(_loadedFiles);
                File.WriteAllText(_filesListPath, json);
            }
            catch
            {
                // Silently fail if we can't save
            }
        }

        private string ExtractShortPeriod(string periodString)
        {
            if (string.IsNullOrWhiteSpace(periodString))
                return "";

            var match = Regex.Match(periodString, @"(\w+)\s*(\d{4})\s*to\s*(\w+)\s*(\d{4})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string startMonth = match.Groups[1].Value.Substring(0, Math.Min(3, match.Groups[1].Value.Length));
                string startYear = match.Groups[2].Value;
                string endMonth = match.Groups[3].Value.Substring(0, Math.Min(3, match.Groups[3].Value.Length));
                string endYear = match.Groups[4].Value;

                if (startYear == endYear)
                    return $"{startMonth}-{endMonth} {startYear}";
                else
                    return $"{startMonth} {startYear}-{endMonth} {endYear}";
            }

            var yearMatch = Regex.Match(periodString, @"\d{4}");
            if (yearMatch.Success)
                return yearMatch.Value;

            return "";
        }

        /// <summary>
        /// Intelligently finds the header row by looking for "Sr No" or similar patterns
        /// </summary>
        private int FindHeaderRow(IXLWorksheet ws)
        {
            // Check rows 1-10 for header patterns
            for (int row = 1; row <= Math.Min(10, ws.LastRowUsed()?.RowNumber() ?? 10); row++)
            {
                var firstCell = ws.Cell(row, 1).GetString().ToLower().Trim();

                // Look for common header indicators
                if (firstCell.Contains("sr") && (firstCell.Contains("no") || firstCell.Contains("num")) ||
                    firstCell == "s.no" || firstCell == "s no" || firstCell == "sr no")
                {
                    return row;
                }
            }

            // Default to row 4 if not found
            return 4;
        }

        /// <summary>
        /// Checks if a sheet has actual student data or is just a template
        /// </summary>
        private bool SheetHasData(IXLWorksheet ws, int headerRow)
        {
            // Check if there's at least one data row after the header
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

            if (lastRow <= headerRow)
                return false;

            // Check if the first data row has actual content
            int dataRow = headerRow + 1;
            if (dataRow > lastRow)
                return false;

            // Count non-empty cells in the first data row
            int nonEmptyCells = 0;
            for (int col = 1; col <= Math.Min(10, ws.LastColumnUsed()?.ColumnNumber() ?? 10); col++)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cell(dataRow, col).GetString()))
                {
                    nonEmptyCells++;
                }
            }

            // If we have at least 3 non-empty cells in the first data row, consider it valid
            return nonEmptyCells >= 3;
        }

        public void LoadFile(string filePath)
        {
            _currentFilePath = filePath;

            if (!_loadedFiles.Contains(filePath))
            {
                _loadedFiles.Add(filePath);
                SaveFilesList();
            }

            using var workbook = new XLWorkbook(filePath);

            foreach (var ws in workbook.Worksheets)
            {
                // Extract metadata from first 3 rows
                string instituteName = ws.Cell(1, 1).GetString();
                string period = ws.Cell(2, 1).GetString();
                string courseInfo = ws.Cell(3, 1).GetString();

                // Find header row intelligently
                int headerRow = FindHeaderRow(ws);

                // Check if sheet has actual data
                bool hasData = SheetHasData(ws, headerRow);

                // CRITICAL FIX: Use square brackets instead of parentheses to avoid WPF binding errors
                string shortPeriod = ExtractShortPeriod(period);
                string displayName = string.IsNullOrWhiteSpace(shortPeriod)
                    ? ws.Name
                    : $"{ws.Name} [{shortPeriod}]";

                var metadata = new SheetMetadata
                {
                    InstituteName = instituteName,
                    Period = period,
                    CourseInfo = courseInfo,
                    DisplayName = displayName,
                    IsEmpty = !hasData
                };

                _metadata[ws.Name] = metadata;

                // Parse notes for auto-increment
                ParseSheetNotes(ws);

                // Skip loading data for empty sheets
                if (!hasData)
                {
                    // Create an empty table for empty sheets
                    var emptyTable = new DataTable(ws.Name);
                    emptyTable.Columns.Add("Info");
                    var emptyRow = emptyTable.NewRow();
                    emptyRow["Info"] = "No student data available for this sheet";
                    emptyTable.Rows.Add(emptyRow);
                    _sheets[ws.Name] = emptyTable;
                    continue;
                }

                // Load actual data
                var table = new DataTable(ws.Name);

                // Read headers
                var headers = new List<string>();
                var headerCells = ws.Row(headerRow).CellsUsed().ToList();

                foreach (var cell in headerCells)
                {
                    string columnName = cell.GetString().Trim();

                    if (string.IsNullOrWhiteSpace(columnName))
                        continue;

                    // Ensure unique column names
                    string uniqueColumnName = columnName;
                    int counter = 1;
                    while (headers.Contains(uniqueColumnName))
                    {
                        uniqueColumnName = $"{columnName}_{counter}";
                        counter++;
                    }

                    headers.Add(uniqueColumnName);
                    table.Columns.Add(uniqueColumnName);
                }

                // Add scholarship column if it doesn't exist
                if (!headers.Any(h => h.ToLower().Contains("scholarship")))
                {
                    table.Columns.Add("Scholarship %");
                }

                // Add phone number column if it doesn't exist
                if (!headers.Any(h => h.ToLower().Contains("phone") || h.ToLower().Contains("mobile")))
                {
                    table.Columns.Add("Phone Number");
                }

                // Read data rows
                int dataStartRow = headerRow + 1;
                var lastRowNum = ws.LastRowUsed()?.RowNumber() ?? dataStartRow;

                for (int rowNum = dataStartRow; rowNum <= lastRowNum; rowNum++)
                {
                    var row = ws.Row(rowNum);

                    // Skip completely empty rows
                    if (!row.CellsUsed().Any())
                        continue;

                    // Check if this is another header (some sheets have multiple sections)
                    var firstCellValue = row.Cell(1).GetString().ToLower().Trim();
                    if (firstCellValue.Contains("central institute") ||
                        firstCellValue.Contains("diploma") ||
                        firstCellValue.Contains("sub:-"))
                    {
                        continue; // Skip metadata rows that appear mid-sheet
                    }

                    var values = new object[table.Columns.Count];

                    // Read existing columns
                    for (int i = 0; i < headers.Count && i < row.CellsUsed().Count(); i++)
                    {
                        var cellValue = row.Cell(i + 1).GetString().Trim();
                        values[i] = cellValue;
                    }

                    // Initialize scholarship if column was added
                    if (table.Columns.Contains("Scholarship %") && !headers.Any(h => h.ToLower().Contains("scholarship")))
                    {
                        values[table.Columns.IndexOf("Scholarship %")] = "0";
                    }

                    table.Rows.Add(values);
                }

                // Only add sheet if it has at least one data row
                if (table.Rows.Count > 0)
                {
                    // Calculate fees for each row
                    foreach (DataRow dataRow in table.Rows)
                    {
                        RecalculateRowFees(ws.Name, dataRow);
                    }

                    _sheets[ws.Name] = table;
                }
                else
                {
                    // Create a placeholder for sheets with no valid data
                    var placeholderTable = new DataTable(ws.Name);
                    placeholderTable.Columns.Add("Info");
                    var placeholderRow = placeholderTable.NewRow();
                    placeholderRow["Info"] = "No valid student records found";
                    placeholderTable.Rows.Add(placeholderRow);
                    _sheets[ws.Name] = placeholderTable;
                }
            }
        }

        public void RecalculateRowFees(string sheetName, DataRow row)
        {
            var table = row.Table;

            var installmentCols = table.Columns.Cast<DataColumn>()
                .Where(c => c.ColumnName.ToLower().Contains("installment") ||
                           c.ColumnName.ToLower().Contains("inst ") ||
                           c.ColumnName.ToLower().Contains("quarterly fees"))
                .ToList();

            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            var previousPendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") ||
                               c.ColumnName.ToLower().Contains("pending"));

            var totalCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("total"));

            var actualPaidCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("actual paid") ||
                               c.ColumnName.ToLower().Contains("paid"));

            var balanceCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("balance"));

            decimal scholarshipPercent = 0;
            if (scholarshipCol != null)
            {
                decimal.TryParse(row[scholarshipCol]?.ToString()?.Trim(), out scholarshipPercent);
            }

            decimal totalFromInstallments = 0;
            foreach (var col in installmentCols)
            {
                if (decimal.TryParse(row[col]?.ToString()?.Trim(), out decimal installmentAmount))
                {
                    totalFromInstallments += installmentAmount;
                }
            }

            decimal scholarshipAmount = totalFromInstallments * (scholarshipPercent / 100);
            decimal totalAfterScholarship = totalFromInstallments - scholarshipAmount;

            decimal previousPending = 0;
            if (previousPendingCol != null)
            {
                decimal.TryParse(row[previousPendingCol]?.ToString()?.Trim(), out previousPending);
            }

            decimal grandTotal = totalAfterScholarship + previousPending;

            if (totalCol != null)
            {
                row[totalCol] = grandTotal.ToString("0.00");
            }

            decimal actualPaid = 0;
            if (actualPaidCol != null)
            {
                decimal.TryParse(row[actualPaidCol]?.ToString()?.Trim(), out actualPaid);
            }

            decimal balance = grandTotal - actualPaid;

            if (balanceCol != null)
            {
                row[balanceCol] = balance.ToString("0.00");
            }
        }

        public void SaveFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                throw new InvalidOperationException("No file loaded.");

            using var workbook = new XLWorkbook(_currentFilePath);

            foreach (var sheet in _sheets)
            {
                var table = sheet.Value;
                var ws = workbook.Worksheets.FirstOrDefault(w => w.Name == sheet.Key);

                if (ws == null) continue;

                // Skip saving for placeholder sheets
                if (table.Columns.Count == 1 && table.Columns[0].ColumnName == "Info")
                    continue;

                if (_metadata.TryGetValue(sheet.Key, out var metadata))
                {
                    ws.Cell(1, 1).Value = metadata.InstituteName ?? "";
                    ws.Cell(2, 1).Value = metadata.Period ?? "";
                    ws.Cell(3, 1).Value = metadata.CourseInfo ?? "";
                }

                int headerRow = FindHeaderRow(ws);

                for (int c = 0; c < table.Columns.Count; c++)
                {
                    ws.Cell(headerRow, c + 1).Value = table.Columns[c].ColumnName;
                    ws.Cell(headerRow, c + 1).Style.Font.Bold = true;
                    ws.Cell(headerRow, c + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                }

                int r = headerRow + 1;
                foreach (DataRow row in table.Rows)
                {
                    for (int c = 0; c < table.Columns.Count; c++)
                    {
                        ws.Cell(r, c + 1).SetValue(row[c]?.ToString() ?? "");
                    }
                    r++;
                }

                ws.Columns().AdjustToContents();
            }

            workbook.SaveAs(_currentFilePath);
        }

        public List<string> GetSheetNames()
        {
            return _sheets.Keys.ToList();
        }

        public List<string> GetSheetDisplayNames()
        {
            var displayNames = new List<string>();
            foreach (var sheetName in _sheets.Keys)
            {
                if (_metadata.TryGetValue(sheetName, out var metadata))
                {
                    // Add indicator for empty sheets
                    string name = metadata.DisplayName;
                    if (metadata.IsEmpty)
                    {
                        name += " [Empty]";
                    }
                    displayNames.Add(name);
                }
                else
                {
                    displayNames.Add(sheetName);
                }
            }
            return displayNames;
        }

        public string GetSheetNameFromDisplay(string displayName)
        {
            // Remove the [Empty] suffix if present
            displayName = displayName.Replace(" [Empty]", "");

            foreach (var kvp in _metadata)
            {
                if (kvp.Value.DisplayName == displayName)
                    return kvp.Key;
            }
            return displayName;
        }

        public DataTable GetSheet(string sheetName)
        {
            return _sheets.TryGetValue(sheetName, out var table) ? table : null;
        }

        public SheetMetadata GetSheetMetadata(string sheetName)
        {
            return _metadata.TryGetValue(sheetName, out var metadata) ? metadata : null;
        }

        public DataView GetPendingFeesView(string sheetName)
        {
            if (!_sheets.TryGetValue(sheetName, out var table))
                return null;

            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("previous"));

            if (pendingCol == null)
                return null;

            var filtered = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                string raw = row[pendingCol]?.ToString()?.Trim();
                if (decimal.TryParse(raw, out decimal pending) && pending > 0)
                {
                    filtered.ImportRow(row);
                }
            }

            return filtered.DefaultView;
        }

        public DataView GetScholarshipView(string sheetName)
        {
            if (!_sheets.TryGetValue(sheetName, out var table))
                return null;

            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            if (scholarshipCol == null)
                return null;

            var filtered = table.Clone();

            foreach (DataRow row in table.Rows)
            {
                string raw = row[scholarshipCol]?.ToString()?.Trim();
                if (decimal.TryParse(raw, out decimal scholarship) && scholarship > 0)
                {
                    filtered.ImportRow(row);
                }
            }

            return filtered.DefaultView;
        }

        public List<string> GetLoadedFiles()
        {
            return _loadedFiles.Where(f => File.Exists(f)).ToList();
        }

        public void RemoveFile(string filePath)
        {
            _loadedFiles.Remove(filePath);
            SaveFilesList();

            var sheetsToRemove = _sheets.Where(kvp => kvp.Key.StartsWith(Path.GetFileNameWithoutExtension(filePath)))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sheet in sheetsToRemove)
            {
                _sheets.Remove(sheet);
                _metadata.Remove(sheet);
                _sheetNotes.Remove(sheet);
            }
        }

        private void ParseSheetNotes(IXLWorksheet ws)
        {
            try
            {
                for (int row = 1; row <= 10; row++)
                {
                    for (int col = 1; col <= 5; col++)
                    {
                        var cellValue = ws.Cell(row, col).GetString();

                        if (string.IsNullOrWhiteSpace(cellValue))
                            continue;

                        var match = Regex.Match(cellValue,
                            @"note:?\s*increment\s*₹?(\d+(?:\.\d+)?)\s*after\s*(\d{1,2}[-/]\d{1,2}[-/]\d{4})",
                            RegexOptions.IgnoreCase);

                        if (match.Success)
                        {
                            decimal incrementAmount = decimal.Parse(match.Groups[1].Value);
                            string dateStr = match.Groups[2].Value;

                            DateTime incrementDate;
                            if (DateTime.TryParseExact(dateStr,
                                new[] { "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy" },
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None,
                                out incrementDate))
                            {
                                _sheetNotes[ws.Name] = new SheetNoteInfo
                                {
                                    IncrementAmount = incrementAmount,
                                    IncrementDate = incrementDate,
                                    RawNote = cellValue
                                };
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If note parsing fails, just continue without notes
            }
        }

        public SheetNoteInfo GetSheetNote(string sheetName)
        {
            return _sheetNotes.TryGetValue(sheetName, out var note) ? note : null;
        }

        /// <summary>
        /// Updates the extension date for a sheet's auto-increment note
        /// </summary>
        public void UpdateExtensionDate(string sheetName, DateTime newDate)
        {
            // Convert display name to actual sheet name if needed
            string actualSheetName = GetSheetNameFromDisplay(sheetName);

            if (_sheetNotes.TryGetValue(actualSheetName, out var noteInfo))
            {
                noteInfo.IncrementDate = newDate;
            }
        }

        /// <summary>
        /// Manually applies the increment from the sheet note to all pending fees
        /// </summary>
        public void ManuallyApplyIncrement(string sheetName)
        {
            // Convert display name to actual sheet name if needed
            string actualSheetName = GetSheetNameFromDisplay(sheetName);

            if (!_sheets.TryGetValue(actualSheetName, out var table))
                return;

            if (!_sheetNotes.TryGetValue(actualSheetName, out var noteInfo))
                return;

            // Find the pending/balance column
            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("balance") ||
                                   c.ColumnName.ToLower().Contains("previous"));

            if (pendingCol == null)
                return;

            // Apply increment to all rows with pending amounts
            foreach (DataRow row in table.Rows)
            {
                string rawValue = row[pendingCol]?.ToString()?.Trim();
                if (decimal.TryParse(rawValue, out decimal currentPending) && currentPending > 0)
                {
                    decimal newAmount = currentPending + noteInfo.IncrementAmount;
                    row[pendingCol] = newAmount.ToString("0.00");

                    // Recalculate total fees for this row
                    RecalculateRowFees(actualSheetName, row);
                }
            }
        }

        /// <summary>
        /// Applies scholarship percentage to a specific student row
        /// </summary>
        public void ApplyScholarship(string sheetName, DataRow row, decimal scholarshipPercentage)
        {
            // Convert display name to actual sheet name if needed
            string actualSheetName = GetSheetNameFromDisplay(sheetName);

            var table = row.Table;

            // Find scholarship column
            var scholarshipCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("scholarship"));

            if (scholarshipCol != null)
            {
                // Update scholarship percentage
                row[scholarshipCol] = scholarshipPercentage.ToString("0.00");
            }

            // Recalculate fees with new scholarship
            RecalculateRowFees(actualSheetName, row);
        }
    }
}