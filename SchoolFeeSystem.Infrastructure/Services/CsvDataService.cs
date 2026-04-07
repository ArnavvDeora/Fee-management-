using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace SchoolFeeSystem.Presentation.Services
{
    public class CsvDataService
    {
        private readonly Dictionary<string, DataSet> _loadedFiles = new();
        private readonly Dictionary<string, string> _filePaths = new();
        private readonly string _persistenceFile;
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
        public AcademicCycleService CycleService { private get; set; }
        public CsvDataService()
        {
            InitializeFeeStructure();

            // Persistence file lives in the same AppData folder as the payment log
            string appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem");
            Directory.CreateDirectory(appDataDir);
            _persistenceFile = System.IO.Path.Combine(appDataDir, "loaded_files.json");

            // Auto-reload any Excel files the admin previously imported
            AutoLoadPersistedFiles();
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

            LoadFileInternal(filePath);

            // Persist the path so this file auto-loads on next startup
            SavePersistedFiles();
        }

        // Shared loader used by both LoadFile() and AutoLoadPersistedFiles().
        // Does NOT call SavePersistedFiles() to avoid redundant writes during startup.
        private void LoadFileInternal(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var workbook = new XLWorkbook(filePath);
            var dataSet = new DataSet();
            string fileKey = Path.GetFileName(filePath);

            // If already loaded (e.g. re-import), replace the old copy
            if (_loadedFiles.ContainsKey(fileKey))
            {
                _loadedFiles.Remove(fileKey);
                _filePaths.Remove(fileKey);
            }

            foreach (var worksheet in workbook.Worksheets)
            {
                var allRows = worksheet.RowsUsed().ToList();
                var headerRowNums = FindAllHeaderRows(allRows);

                if (headerRowNums.Count == 0) continue;

                for (int b = 0; b < headerRowNums.Count; b++)
                {
                    int headerNum = headerRowNums[b];
                    int nextHeader = b + 1 < headerRowNums.Count
                                         ? headerRowNums[b + 1] : int.MaxValue;

                    string courseRow = ExtractSubTableCourseRow(allRows, headerNum, b == 0);
                    var metadata = ExtractMetadataFromSubTable(worksheet, allRows, headerNum, courseRow);
                    DataTable table = BuildDataTableForBlock(allRows, headerNum, nextHeader, worksheet.Name);

                    string baseName = headerRowNums.Count == 1
                        ? worksheet.Name
                        : $"{worksheet.Name}_b{b}_Sem{metadata.Semester}";

                    string uniqueName = baseName;
                    int collision = 2;
                    while (dataSet.Tables.Contains(uniqueName))
                        uniqueName = $"{baseName}_c{collision++}";

                    table.TableName = uniqueName;

                    table.ExtendedProperties["Department"] = metadata.DepartmentCode;
                    table.ExtendedProperties["Year"] = metadata.Year;
                    table.ExtendedProperties["Semester"] = metadata.Semester.ToString();
                    table.ExtendedProperties["Quarter"] = metadata.Quarter;
                    table.ExtendedProperties["Period"] = metadata.Period;
                    table.ExtendedProperties["CourseInfo"] = metadata.CourseInfo;
                    table.ExtendedProperties["InstituteName"] = metadata.InstituteName;
                    table.ExtendedProperties["OriginalSheetName"] = worksheet.Name;

                    dataSet.Tables.Add(table);
                    CycleService?.RecordFileImport(table);
                }
            }

            _loadedFiles[fileKey] = dataSet;
            _filePaths[fileKey] = filePath;
        }

        // ── Find the "Diploma - ..." / "Sub:-" course-description row for one block ─
        // For the first block: row 3 of the worksheet is the standard course title.
        // For later blocks: scan up to 8 rows above the header for any row whose
        //                   first cell mentions a course/diploma/semester keyword.
        //                   Falls back to a guaranteed-unique placeholder so the
        //                   collision-proof naming loop in LoadFile always works.
        private string ExtractSubTableCourseRow(List<IXLRow> allRows, int headerRowNum, bool isFirstBlock)
        {
            if (isFirstBlock)
            {
                var r3 = allRows.FirstOrDefault(r => r.RowNumber() == 3);
                return r3?.CellsUsed().FirstOrDefault()?.GetString()?.Trim() ?? "";
            }

            // Scan up to 8 rows above the header — widen the window so we catch
            // course titles that sit further away (e.g. with a blank row between).
            for (int offset = 1; offset <= 8; offset++)
            {
                int targetNum = headerRowNum - offset;
                if (targetNum < 1) break;

                var row = allRows.FirstOrDefault(r => r.RowNumber() == targetNum);
                if (row == null) continue;

                string val = row.CellsUsed().FirstOrDefault()?.GetString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(val)) continue;

                // Accept any row that describes a course/section
                if (val.StartsWith("Diploma", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("Course", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("Sub:-", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("Sub :-", StringComparison.OrdinalIgnoreCase) ||
                    val.IndexOf("Semester", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    val.IndexOf("Sem.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    val.IndexOf("Year", StringComparison.OrdinalIgnoreCase) >= 0)
                    return val;
            }

            // If nothing useful found above, return empty string — the metadata
            // extractor will fall back to Semester=0 and the naming loop will still
            // generate a unique name via the _b{N} collision suffix.
            return "";
        }

        // ── Metadata extraction scoped to one sub-table (Bug 2 fix) ─────────────
        // Each block reads its own course-description row, so "5th Semester" yields
        // Semester=5, Year=3 — independent of what other blocks in the sheet say.
        private SheetMetadataExtended ExtractMetadataFromSubTable(
            IXLWorksheet worksheet,
            List<IXLRow> allRows,
            int headerRowNum,
            string courseRow)
        {
            var metadata = new SheetMetadataExtended();

            // Institute name is always worksheet row 1
            metadata.InstituteName = allRows.FirstOrDefault(r => r.RowNumber() == 1)
                ?.CellsUsed().FirstOrDefault()?.GetString()?.Trim() ?? "";

            // Period / quarter: scan all cells in the 5 rows above this block's header
            string monthPattern =
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER" +
                @"|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}" +
                @"\s+[Tt][Oo]\s+" +
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER" +
                @"|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}";

            for (int offset = 5; offset >= 0; offset--)
            {
                int rowNum = headerRowNum - offset;
                if (rowNum < 1) continue;

                var row = allRows.FirstOrDefault(r => r.RowNumber() == rowNum);
                if (row == null) continue;

                foreach (var cell in row.CellsUsed())
                {
                    string cellText = cell.GetString()?.Trim() ?? "";
                    var m = System.Text.RegularExpressions.Regex.Match(
                        cellText, monthPattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        metadata.Period = m.Value;
                        metadata.Quarter = DeterminQuarter(m.Value);
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(metadata.Period)) break;
            }

            // Department detection from this block's course-description row
            metadata.CourseInfo = courseRow;
            string courseText = courseRow.ToUpper();

            if (courseText.Contains("PASSOUT") || courseText.Contains("PASS OUT")) metadata.DepartmentCode = "PASSOUT";
            else if (courseText.Contains("MECHATRONICS")) metadata.DepartmentCode = "MECHATRONICS";
            else if (courseText.Contains("MECHANICAL") || courseText.Contains("(T&D)") ||
                     courseText.Contains("TOOL AND DIE") || courseText.Contains("TOOL & DIE") ||
                     courseText.Contains(" ME ") || courseText.Contains("M.E")) metadata.DepartmentCode = "ME";
            else if (courseText.Contains("ELECTRICAL")) metadata.DepartmentCode = "EE";
            else if (courseText.Contains("COMPUTER") || courseText.Contains("CSE") ||
                     courseText.Contains("C.S.E")) metadata.DepartmentCode = "CSE";
            else metadata.DepartmentCode = "MISC";

            // ── Semester → Year conversion (the key fix for Bug 2) ────────────────
            // Parse "3RD SEM", "5th Semester", "2nd Year" from THIS block's course row.
            // Formula: Year = ceil(Sem / 2)  →  Sem 3 = Year 2, Sem 5 = Year 3.
            var semMatch = System.Text.RegularExpressions.Regex.Match(
                courseRow,
                @"(\d+)\s*(?:st|nd|rd|th)\s+(?:Sem(?:ester)?|Year)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (semMatch.Success)
            {
                int semNum = int.Parse(semMatch.Groups[1].Value);
                metadata.Semester = semNum;
                metadata.Year = ((semNum + 1) / 2).ToString();
            }
            else
            {
                // Try bare "Xth Year" / "Xnd Year" without the Sem/Semester keyword
                var yrMatch = System.Text.RegularExpressions.Regex.Match(
                    courseRow,
                    @"(\d+)(?:st|nd|rd|th)\s+Year",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (yrMatch.Success)
                {
                    int yr = int.Parse(yrMatch.Groups[1].Value);
                    metadata.Year = yr.ToString();
                    metadata.Semester = yr * 2;   // approximate: 2nd Year ≈ Sem 4
                }
                else
                {
                    metadata.Year = "1";
                    metadata.Semester = 1;
                }
            }

            return metadata;
        }

        // ── Build a DataTable for exactly one block (from header row to next header) ─
        private DataTable BuildDataTableForBlock(
            List<IXLRow> allRows,
            int headerRowNum,
            int nextHeaderRowNum,
            string worksheetName)
        {
            // Column list from this block's header row
            var headerRow = allRows.First(r => r.RowNumber() == headerRowNum);
            var blockCols = new List<(string Name, int ColAddr)>();
            foreach (var cell in headerRow.CellsUsed())
            {
                string colName = cell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(colName))
                    colName = $"Column{cell.Address.ColumnNumber}";
                blockCols.Add((colName, cell.Address.ColumnNumber));
            }

            // Build DataTable with _Section tag + block columns (dedup)
            var table = new DataTable();
            table.Columns.Add("_Section");

            var seenCols = new List<string>();
            foreach (var (name, _) in blockCols)
            {
                string finalName = name;
                int suffix = 2;
                while (seenCols.Contains(finalName, StringComparer.OrdinalIgnoreCase))
                    finalName = $"{name}_{suffix++}";
                seenCols.Add(finalName);
                table.Columns.Add(finalName);
            }

            string sectionLabel = DeriveSectionLabel(allRows, headerRowNum);
            int nameColAddr = blockCols.Count > 1 ? blockCols[1].ColAddr : -1;

            foreach (var row in allRows)
            {
                int rowNum = row.RowNumber();
                if (rowNum <= headerRowNum) continue;
                if (rowNum >= nextHeaderRowNum) break;

                // Skip non-student rows
                if (nameColAddr >= 0)
                {
                    string nameVal = row.Cell(nameColAddr).GetString().Trim();
                    if (string.IsNullOrEmpty(nameVal)) continue;
                    if (nameVal.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nameVal.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nameVal.Length > 60 || nameVal.Contains(":-") ||
                        nameVal.Contains("Per Day")) continue;
                }

                var dataRow = table.NewRow();
                dataRow["_Section"] = sectionLabel;

                for (int ci = 0; ci < blockCols.Count; ci++)
                {
                    int colAddr = blockCols[ci].ColAddr;
                    var cell = row.Cell(colAddr);
                    string cellValue;
                    if (cell.HasFormula)
                    {
                        try { cellValue = cell.CachedValue.ToString()?.Trim() ?? ""; }
                        catch { cellValue = ""; }
                    }
                    else
                    {
                        cellValue = cell.GetString().Trim();
                    }
                    dataRow[ci + 1] = cellValue;   // +1: col 0 is _Section
                }

                table.Rows.Add(dataRow);
            }

            return table;
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

        // -----------------------------------------------------------------------
        // MULTI-TABLE AWARE WORKSHEET LOADING
        //
        // Many sheets in this school's Excel format contain TWO sub-tables on
        // the same sheet (e.g. "Regular Students" + "NEW ADMISSION LEET").
        // Both share the same quarter/period but have different column sets.
        //
        // Strategy:
        //   1. Find ALL header rows in the sheet (rows containing "Sr No." + "Name").
        //   2. For each header-row block, extract only the student rows that belong
        //      to that block (stop when the next header row / end of sheet is hit).
        //   3. Merge all blocks into a single DataTable using a superset of all
        //      columns, tagged with "_Section" so the UI can group them.
        //   4. Only rows with a non-empty "Name" column are treated as students —
        //      this eliminates gap rows, SUM rows, section-label rows, and NOTE rows.
        // -----------------------------------------------------------------------

        private DataTable WorksheetToDataTable(IXLWorksheet worksheet)
        {
            // Step 1: Locate ALL header rows in the sheet
            var allRows = worksheet.RowsUsed().ToList();
            var headerRowNums = FindAllHeaderRows(allRows);

            if (headerRowNums.Count == 0)
            {
                // Sheet has no recognisable header — return empty table
                return new DataTable(worksheet.Name);
            }

            // Step 2: Parse each sub-table block into (columns, dataRows) pairs
            var blocks = new List<(List<(string Name, int ColAddr)> Cols, List<List<string>> Rows, string Section)>();

            for (int b = 0; b < headerRowNums.Count; b++)
            {
                int headerNum = headerRowNums[b];
                int nextHeader = b + 1 < headerRowNums.Count ? headerRowNums[b + 1] : int.MaxValue;

                // Derive a section label — look for a section title row just above
                // the header (single non-formula, non-empty cell, not a metadata row).
                string sectionLabel = DeriveSectionLabel(allRows, headerNum);

                // Build column list for this block
                var headerRow = allRows.First(r => r.RowNumber() == headerNum);
                var blockCols = new List<(string Name, int ColAddr)>();
                foreach (var cell in headerRow.CellsUsed())
                {
                    string colName = cell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(colName))
                        colName = $"Column{cell.Address.ColumnNumber}";
                    blockCols.Add((colName, cell.Address.ColumnNumber));
                }

                // Collect student rows for this block
                var blockDataRows = new List<List<string>>();
                foreach (var row in allRows)
                {
                    int rowNum = row.RowNumber();
                    if (rowNum <= headerNum) continue; // skip header and above
                    if (rowNum >= nextHeader) break;    // stop at next block's header

                    // A valid student row MUST have a non-empty Name cell
                    // (2nd column in the header = "Name", typically column B)
                    int nameColAddr = blockCols.Count > 1 ? blockCols[1].ColAddr : -1;
                    if (nameColAddr < 0) continue;

                    string nameVal = row.Cell(nameColAddr).GetString().Trim();
                    if (string.IsNullOrEmpty(nameVal)) continue; // gap, SUM, label rows

                    // Skip repeated sub-header rows (Name cell literally says "Name")
                    if (nameVal.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;

                    // Skip Note/disclaimer rows — the school puts these in column B (Name col)
                    // They are long sentences and always start with "Note"
                    if (nameVal.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;

                    // A real student's Sr No. (column A) must be a positive integer.
                    // If it is empty or non-numeric the row is a footer/total/label — skip it.
                    int srNoColAddr = blockCols.Count > 0 ? blockCols[0].ColAddr : -1;
                    if (srNoColAddr >= 0)
                    {
                        string srNoVal = row.Cell(srNoColAddr).GetString().Trim();
                        // Allow empty Sr No. only if Name is clearly a person (short, no punctuation)
                        bool srIsNumber = int.TryParse(srNoVal, out _);
                        bool nameIsSentence = nameVal.Length > 60 || nameVal.Contains(":-") ||
                                             nameVal.Contains("Per Day") || nameVal.Contains("deposited");
                        if (nameIsSentence) continue; // definitely not a student name
                        if (!srIsNumber && !string.IsNullOrEmpty(srNoVal)) continue; // non-numeric Sr No.
                    }

                    // Collect cell values for all columns of this block
                    var rowValues = new List<string>();
                    foreach (var (_, colAddr) in blockCols)
                    {
                        var cell = row.Cell(colAddr);
                        string cellValue;
                        if (cell.HasFormula)
                        {
                            try { cellValue = cell.CachedValue.ToString()?.Trim() ?? ""; }
                            catch { cellValue = ""; }
                        }
                        else
                        {
                            cellValue = cell.GetString().Trim();
                        }
                        rowValues.Add(cellValue);
                    }
                    blockDataRows.Add(rowValues);
                }

                if (blockDataRows.Count > 0 || blocks.Count == 0)
                    blocks.Add((blockCols, blockDataRows, sectionLabel));
            }

            // Step 3: Build a unified DataTable with the superset of all columns.
            // We add a hidden "_Section" column so the UI can optionally group rows.
            var table = new DataTable(worksheet.Name);
            table.Columns.Add("_Section"); // internal grouping tag, hidden in UI

            // Collect superset of column names in encounter order
            var allColNames = new List<string>();
            foreach (var (cols, _, _) in blocks)
            {
                foreach (var (name, _) in cols)
                {
                    if (!allColNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        allColNames.Add(name);
                }
            }

            // Deduplicate and add to table
            foreach (var colName in allColNames)
            {
                string finalName = colName;
                int suffix = 2;
                while (table.Columns.Contains(finalName))
                    finalName = $"{colName}_{suffix++}";
                table.Columns.Add(finalName);
            }

            // Step 4: Fill rows from each block
            foreach (var (blockCols, blockRows, section) in blocks)
            {
                // Map this block's column names to DataTable column indices
                var colIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int ci = 0; ci < blockCols.Count; ci++)
                {
                    string blockColName = blockCols[ci].Name;
                    // Find matching DataTable column
                    for (int di = 1; di < table.Columns.Count; di++) // skip _Section at 0
                    {
                        if (table.Columns[di].ColumnName.StartsWith(blockColName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            colIndexMap[blockColName] = di;
                            break;
                        }
                    }
                }

                foreach (var rowValues in blockRows)
                {
                    var dataRow = table.NewRow();
                    dataRow["_Section"] = section;
                    for (int ci = 0; ci < blockCols.Count && ci < rowValues.Count; ci++)
                    {
                        string blockColName = blockCols[ci].Name;
                        if (colIndexMap.TryGetValue(blockColName, out int dtColIdx))
                            dataRow[dtColIdx] = rowValues[ci];
                    }
                    table.Rows.Add(dataRow);
                }
            }

            return table;
        }

        /// <summary>
        /// Finds ALL row numbers in the worksheet that look like a student-data header
        /// (must contain both "Sr No." and "Name" cells).
        /// </summary>
        private List<int> FindAllHeaderRows(List<IXLRow> allRows)
        {
            var result = new List<int>();
            foreach (var row in allRows)
            {
                var cellTexts = row.CellsUsed()
                                   .Select(c => c.GetString().Trim())
                                   .ToList();

                bool hasSrNo = cellTexts.Any(c =>
                    c.Equals("Sr No.", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("Sr No", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("Sr.", StringComparison.OrdinalIgnoreCase));

                bool hasName = cellTexts.Any(c =>
                    c.Equals("Name", StringComparison.OrdinalIgnoreCase));

                if (hasSrNo && hasName)
                    result.Add(row.RowNumber());
            }

            // Fallback: if no header found, try the row with the most cells after row 3
            if (result.Count == 0)
            {
                int maxCells = 0, bestRow = 5;
                foreach (var row in allRows)
                {
                    if (row.RowNumber() <= 3) continue;
                    int count = row.CellsUsed().Count();
                    if (count <= 1) continue;
                    if (count > maxCells) { maxCells = count; bestRow = row.RowNumber(); }
                }
                result.Add(bestRow);
            }

            return result;
        }

        /// <summary>
        /// Looks immediately above a header row for a section title label
        /// (a single-cell, non-formula row that isn't a metadata row 1-3).
        /// </summary>
        private string DeriveSectionLabel(List<IXLRow> allRows, int headerRowNum)
        {
            // Search up to 4 rows above the header for a label row
            for (int offset = 1; offset <= 4; offset++)
            {
                int targetRow = headerRowNum - offset;
                if (targetRow <= 3) break;

                var row = allRows.FirstOrDefault(r => r.RowNumber() == targetRow);
                if (row == null) continue;

                var usedCells = row.CellsUsed().ToList();
                if (usedCells.Count != 1) continue; // only single-cell rows qualify

                string val = usedCells[0].GetString().Trim();
                if (string.IsNullOrEmpty(val)) continue;
                if (val.StartsWith("=")) continue;                          // formula
                if (val.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue; // note
                if (val.StartsWith("Sub:-", StringComparison.OrdinalIgnoreCase)) continue; // period text

                return val; // e.g. "NEW ADMISSION 2024 LEET", "LEET", etc.
            }

            return "Regular"; // default label for the first (main) table
        }

        // FindHeaderRow kept as the single-result convenience wrapper used by ExtractMetadataFromSheet
        private int FindHeaderRow(IXLWorksheet worksheet)
        {
            var allRows = worksheet.RowsUsed().ToList();
            var allHeaders = FindAllHeaderRows(allRows);
            return allHeaders.Count > 0 ? allHeaders[0] : 5;
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

        // GetSheetsByDepartment is defined below (returns List<DataTable>)

        // GetSheetByFilter is defined below with (departmentCode, year, quarter) parameters

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

        // RecalculateRowFees is defined below with full fine logic

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
            SavePersistedFiles();   // keep persistence list in sync
        }

        public List<string> GetSheetDisplayNames()
        {
            var names = new List<string>();
            foreach (var kvp in _loadedFiles)
            {
                foreach (DataTable table in kvp.Value.Tables)
                {
                    // If the admin has assigned a custom display name, use it.
                    // Otherwise fall back to the original "FileName - SheetName" format.
                    string customName = table.ExtendedProperties["DisplayName"]?.ToString();
                    string displayName = !string.IsNullOrWhiteSpace(customName)
                        ? customName
                        : $"{Path.GetFileNameWithoutExtension(kvp.Key)} - {table.TableName}";

                    names.Add(displayName);
                }
            }
            return names;
        }

        public string GetSheetNameFromDisplay(string displayName)
        {
            // First try to find a table whose custom DisplayName matches exactly.
            foreach (var kvp in _loadedFiles)
                foreach (DataTable table in kvp.Value.Tables)
                {
                    string custom = table.ExtendedProperties["DisplayName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(custom) &&
                        custom.Equals(displayName, StringComparison.OrdinalIgnoreCase))
                        return table.TableName;
                }

            // Fall back to the original "FileName - SheetName" split.
            var parts = displayName.Split(new[] { " - " }, StringSplitOptions.None);
            return parts.Length > 1 ? parts[1] : displayName;
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

        // SaveFile is defined below with full payment history support


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
                CourseInfo = sheetName,
                Quarter = table.ExtendedProperties["Quarter"]?.ToString() ?? ""
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
        // ===========================================
        // DEPARTMENT & YEAR FILTERING
        // ===========================================

        /// <summary>
        /// Get a sheet by department code, year, and quarter
        /// Example: GetSheetByFilter("ME", 1, "Aug-Oct") returns "Mechanical-1st-AugOct" sheet
        /// </summary>
        // GetSheetByFilter(departmentCode, year, quarter) is defined below

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

        // GetAvailableAcademicYears is defined below

        // ExtractYearFromTableName is defined below (used by GetAvailableAcademicYears)

        // ===========================================
        // YEAR PROGRESSION
        // ===========================================

        // PromoteStudentsToNextYear is defined below

        // ===========================================
        // COURSE & QUARTER DETECTION FROM EXCEL
        // ===========================================

        /// <summary>
        /// Enhanced file loading with better course/year/quarter detection.
        /// Delegates to LoadFile which now handles per-sub-table splitting.
        /// </summary>
        public void LoadFileEnhanced(string filePath)
        {
            // LoadFile now contains all the per-block splitting logic,
            // so LoadFileEnhanced simply calls through to it.
            LoadFile(filePath);
        }

        private SheetMetadataExtended ExtractMetadataFromSheet(IXLWorksheet worksheet)
        {
            var metadata = new SheetMetadataExtended();

            // Read first 5 rows — metadata can be spread across rows 1-5 in different sheets
            var headerLines = new List<string>();
            foreach (var row in worksheet.RowsUsed().Take(5))
                headerLines.Add(row.CellsUsed().FirstOrDefault()?.GetString()?.Trim() ?? "");

            string row1 = headerLines.Count > 0 ? headerLines[0] : "";
            string row2 = headerLines.Count > 1 ? headerLines[1] : "";
            string row3 = headerLines.Count > 2 ? headerLines[2] : "";

            metadata.InstituteName = row1;

            // --- Period & Quarter extraction ---
            // Try row 2 first, then row 3 if row 2 doesn't have period info.
            // Pattern: matches both short (FEB, APR) AND full (FEBRUARY, APRIL) month names,
            // with optional spaces/punctuation, e.g. "(FEB 2026 to APRIL 2026)"
            string monthPattern =
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER" +
                @"|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}" +
                @"\s+[Tt][Oo]\s+" +
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER" +
                @"|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}";

            string periodSource = "";
            foreach (string candidate in new[] { row2, row3, row1 })
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    candidate, monthPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    periodSource = m.Value;
                    metadata.Period = m.Value;
                    metadata.Quarter = DeterminQuarter(m.Value);
                    break;
                }
            }

            // If regex still failed, try a simpler search of row 2 for any known month word
            if (string.IsNullOrEmpty(metadata.Quarter) || metadata.Quarter == "Unknown")
            {
                string combined = (row2 + " " + row3).ToUpper();
                metadata.Quarter = DeterminQuarter(combined);
                if (metadata.Quarter != "Unknown")
                    metadata.Period = combined.Trim();
            }

            // --- Course info & Department extraction ---
            metadata.CourseInfo = row3;

            // Row 3 always has the course description: "Diploma - ME (T&D) - 2nd Year - ..."
            string courseText = row3.ToUpper();

            if (courseText.Contains("PASSOUT") || courseText.Contains("PASS OUT") ||
                courseText.Contains("PASS-OUT"))
                metadata.DepartmentCode = "PASSOUT";
            else if (courseText.Contains("MECHATRONICS"))
                metadata.DepartmentCode = "MECHATRONICS";
            else if (courseText.Contains("MECHANICAL") || courseText.Contains("M.E") ||
                     courseText.Contains(" ME ") || courseText.Contains("(T&D)") ||
                     courseText.Contains("TOOL AND DIE") || courseText.Contains("TOOL & DIE"))
                metadata.DepartmentCode = "ME";
            else if (courseText.Contains("ELECTRICAL"))
                metadata.DepartmentCode = "EE";
            else if (courseText.Contains("COMPUTER") || courseText.Contains("CSE") ||
                     courseText.Contains("C.S.E"))
                metadata.DepartmentCode = "CSE";
            else
                metadata.DepartmentCode = "MISC";

            // --- Year extraction from course info ---
            // Matches: "2nd Year", "3RD Year", "4th Year", "2ND SEM", "5th Semester", "6th Sem"
            var yearMatch = System.Text.RegularExpressions.Regex.Match(
                row3,
                @"(\d+)\s*(?:st|nd|rd|th)\s+(?:Year|Sem(?:ester)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (yearMatch.Success)
            {
                int semNum = int.Parse(yearMatch.Groups[1].Value);
                // Convert semester number to year: 1st/2nd sem = Year 1, 3rd/4th = Year 2, etc.
                if (semNum <= 2) metadata.Year = "1";
                else if (semNum <= 4) metadata.Year = "2";
                else if (semNum <= 6) metadata.Year = "3";
                else metadata.Year = "4";
            }
            else
            {
                // Try direct "Xnd Year" format
                var directYear = System.Text.RegularExpressions.Regex.Match(
                    row3, @"(\d+)(?:st|nd|rd|th)\s+Year",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (directYear.Success)
                    metadata.Year = directYear.Groups[1].Value;
            }

            return metadata;
        }

        private string DeterminQuarter(string period)
        {
            string p = period.ToUpper();

            // Check for the START month of the period (first month in "X to Y")
            // Using full names AND abbreviations since sheets use both.
            // Aug-Oct quarter
            if (p.Contains("AUG") || p.Contains("AUGUST") ||
                p.Contains("SEP") || p.Contains("SEPTEMBER") ||
                p.Contains("OCT") || p.Contains("OCTOBER"))
            {
                return "Aug-Oct";
            }
            // Nov-Jan quarter
            if (p.Contains("NOV") || p.Contains("NOVEMBER") ||
                p.Contains("DEC") || p.Contains("DECEMBER") ||
                p.Contains("JAN") || p.Contains("JANUARY"))
            {
                return "Nov-Jan";
            }
            // Feb-Apr quarter
            if (p.Contains("FEB") || p.Contains("FEBRUARY") ||
                p.Contains("MAR") || p.Contains("MARCH") ||
                p.Contains("APR") || p.Contains("APRIL"))
            {
                return "Feb-Apr";
            }
            // May-Jun quarter
            if (p.Contains("MAY") ||
                p.Contains("JUN") || p.Contains("JUNE"))
            {
                return "May-Jun";
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
            public int Semester { get; set; } = 0;   // Bug 2 fix: per-block semester
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
            public string Quarter { get; set; }
        }
        /// <summary>
        /// Gets all available sheets from all loaded files
        /// </summary>
        public List<DataTable> GetAllSheets()
        {
            var allSheets = new List<DataTable>();

            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    allSheets.Add(table);
                }
            }

            return allSheets;
        }

        /// <summary>
        /// Gets sheets filtered by department code
        /// </summary>
        public List<DataTable> GetSheetsByDepartment(string departmentCode)
        {
            var sheets = new List<DataTable>();

            foreach (var table in GetAllSheets())
            {
                string tableDept = ExtractDepartmentCodeFromTable(table);
                if (tableDept == departmentCode)
                {
                    sheets.Add(table);
                }
            }

            return sheets;
        }

        /// <summary>
        /// Extract department code — reads ExtendedProperties first, then tab name.
        /// </summary>
        private string ExtractDepartmentCodeFromTable(DataTable table)
        {
            // Trust the metadata stored during load
            string metaDept = table.ExtendedProperties["Department"]?.ToString();
            if (!string.IsNullOrEmpty(metaDept) && metaDept != "General")
                return metaDept;

            // Fallback: parse tab name
            string name = table.TableName.ToUpper();
            if (name.Contains("PASSOUT") || name.Contains("PASS OUT")) return "PASSOUT";
            if (name.Contains("MECHATRONICS")) return "MECHATRONICS";
            if (name.Contains("ME") || name.Contains("T&D") || name.Contains("MECH")) return "ME";
            if (name.Contains("EE") || name.Contains("ELECTRICAL")) return "EE";
            if (name.Contains("CSE") || name.Contains("CS") || name.Contains("COMPUTER")) return "CSE";
            return null;
        }

        /// <summary>
        /// Promote students to next year — uses ExtendedProperties year metadata.
        /// </summary>
        public void PromoteStudentsToNextYear(string departmentCode, int currentYear, bool isLastYear)
        {
            try
            {
                // Get sheets whose ExtendedProperties Year matches currentYear
                var currentYearSheets = GetSheetsByDepartment(departmentCode)
                    .Where(t => ExtractYearFromTable(t) == currentYear)
                    .ToList();

                if (!currentYearSheets.Any())
                {
                    // Friendly message listing what years were actually found
                    var foundYears = GetSheetsByDepartment(departmentCode)
                        .Select(t => ExtractYearFromTable(t))
                        .Where(y => y > 0)
                        .Distinct()
                        .OrderBy(y => y)
                        .ToList();
                    string found = foundYears.Any()
                        ? "Found years: " + string.Join(", ", foundYears)
                        : "No sheets found for this department.";
                    throw new Exception(
                        $"No data found for {departmentCode} Year {currentYear}. {found}");
                }

                foreach (var sheet in currentYearSheets)
                {
                    int nextYear = isLastYear ? 0 : currentYear + 1;
                    string targetDept = isLastYear ? "PASSOUT" : departmentCode;

                    // Clone the sheet structure
                    DataTable newSheet = sheet.Clone();
                    newSheet.TableName = GenerateNewSheetName(sheet.TableName, nextYear, targetDept);

                    // Copy student data rows
                    foreach (DataRow row in sheet.Rows)
                    {
                        DataRow newRow = newSheet.NewRow();
                        newRow.ItemArray = (object[])row.ItemArray.Clone();
                        newSheet.Rows.Add(newRow);
                    }

                    // Reset fee columns; carry forward pending fees
                    ResetFeesForNewYear(newSheet);

                    // Update metadata ExtendedProperties for the new sheet
                    newSheet.ExtendedProperties["Department"] = targetDept;
                    newSheet.ExtendedProperties["Year"] = nextYear.ToString();
                    newSheet.ExtendedProperties["Quarter"] = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
                    newSheet.ExtendedProperties["Period"] = sheet.ExtendedProperties["Period"]?.ToString() ?? "";

                    AddSheetToLoadedFiles(newSheet, targetDept);
                }

                SaveFile();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to promote students: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets sheet by filter criteria
        /// </summary>
        public DataTable GetSheetByFilter(string departmentCode, int year, string quarter)
        {
            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (TableMatchesFilter(table, departmentCode, year, quarter))
                    {
                        return table;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if table matches filter criteria
        /// </summary>
        private bool TableMatchesFilter(DataTable table, string departmentCode, int year, string quarter)
        {
            string tableName = table.TableName.ToLower();
            string deptCode = ExtractDepartmentCodeFromTable(table);

            // Check department
            if (deptCode != departmentCode) return false;

            // Check year
            if (!tableName.Contains($"-{year}-") &&
                !tableName.Contains($"year{year}") &&
                !tableName.Contains($"{year}year"))
                return false;

            // Check quarter
            string normalizedQuarter = quarter.Replace("-", "").ToLower();
            if (!tableName.Contains(normalizedQuarter))
                return false;

            return true;
        }

        /// <summary>
        /// Get available academic years for a department — reads from ExtendedProperties.
        /// </summary>
        public List<int> GetAvailableAcademicYears(string departmentCode)
        {
            var years = new HashSet<int>();
            foreach (var table in GetSheetsByDepartment(departmentCode))
            {
                int year = ExtractYearFromTable(table);
                if (year > 0)
                    years.Add(year);
            }
            return years.OrderBy(y => y).ToList();
        }

        /// <summary>
        /// Extract year from a DataTable — first reads the metadata stored by LoadFileEnhanced,
        /// then falls back to parsing the sheet tab name.
        /// </summary>
        private int ExtractYearFromTableName(string tableName)
        {
            // Try to find the table and read its ExtendedProperties first
            foreach (var ds in _loadedFiles.Values)
            {
                if (ds.Tables.Contains(tableName))
                {
                    var tbl = ds.Tables[tableName];
                    if (tbl.ExtendedProperties.ContainsKey("Year") &&
                        int.TryParse(tbl.ExtendedProperties["Year"]?.ToString(), out int metaYear)
                        && metaYear > 0)
                        return metaYear;
                }
            }

            // Fallback: parse from tab name (e.g. "ME-2-FebApr", "MECHATRONICS-3-AugOct")
            string name = tableName.ToLower();
            for (int i = 1; i <= 6; i++)
            {
                if (name.Contains($"-{i}-") || name.Contains($"year{i}") ||
                    name.Contains($"{i}year") || name.Contains($"{i}st") ||
                    name.Contains($"{i}nd") || name.Contains($"{i}rd") ||
                    name.Contains($"{i}th"))
                    return i;
            }
            return 0;
        }

        /// <summary>
        /// Extract year directly from a DataTable object (uses ExtendedProperties).
        /// </summary>
        private int ExtractYearFromTable(DataTable table)
        {
            if (table.ExtendedProperties.ContainsKey("Year") &&
                int.TryParse(table.ExtendedProperties["Year"]?.ToString(), out int yr) && yr > 0)
                return yr;
            return ExtractYearFromTableName(table.TableName);
        }

        /// <summary>
        /// Generate new sheet name for promoted students
        /// </summary>
        private string GenerateNewSheetName(string oldName, int newYear, string newDept)
        {
            // Extract quarter from old name
            string quarter = "AugOct"; // default
            string lowerName = oldName.ToLower();

            if (lowerName.Contains("novjan")) quarter = "NovJan";
            else if (lowerName.Contains("febapr")) quarter = "FebApr";
            else if (lowerName.Contains("mayjun")) quarter = "MayJun";
            else if (lowerName.Contains("augoct")) quarter = "AugOct";

            if (newDept == "PASSOUT")
            {
                return $"PASSOUT-{DateTime.Now.Year}-{quarter}";
            }
            else
            {
                return $"{newDept}-{newYear}-{quarter}";
            }
        }

        /// <summary>
        /// Reset fees for new academic year
        /// </summary>
        private void ResetFeesForNewYear(DataTable table)
        {
            // Find fee-related columns
            var feeColumns = table.Columns.Cast<DataColumn>()
                .Where(c => c.ColumnName.ToLower().Contains("fee") ||
                           c.ColumnName.ToLower().Contains("paid") ||
                           c.ColumnName.ToLower().Contains("amount"))
                .ToList();

            // Find previous pending column
            var previousPendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("previous") &&
                                   c.ColumnName.ToLower().Contains("pending"));

            // Find carried forward column (or create it)
            var carriedForwardCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("carried") &&
                                   c.ColumnName.ToLower().Contains("forward"));

            if (carriedForwardCol == null && previousPendingCol != null)
            {
                carriedForwardCol = table.Columns.Add("Carried Forward", typeof(decimal));
            }

            foreach (DataRow row in table.Rows)
            {
                // Move previous pending to carried forward
                if (previousPendingCol != null && carriedForwardCol != null)
                {
                    row[carriedForwardCol] = row[previousPendingCol];
                }

                // Reset all fee columns to 0
                foreach (var col in feeColumns)
                {
                    if (col != carriedForwardCol)
                    {
                        row[col] = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Add sheet to loaded files
        /// </summary>
        public void AddSheetToLoadedFiles(DataTable sheet, string departmentCode)
        {
            // Find appropriate file or create new one
            string fileKey = $"{departmentCode}_Data.xlsx";

            if (!_loadedFiles.ContainsKey(fileKey))
            {
                _loadedFiles[fileKey] = new DataSet();

                // Set file path
                string filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SchoolFeeData",
                    fileKey);
                _filePaths[fileKey] = filePath;
            }

            _loadedFiles[fileKey].Tables.Add(sheet);
        }

        /// <summary>
        /// Recalculate fees for a specific row
        /// </summary>
        public void RecalculateRowFees(string sheetName, DataRow row)
        {
            // Find fee columns
            var table = row.Table;

            // Basic calculation logic
            decimal totalFee = 0;
            decimal totalPaid = 0;

            foreach (DataColumn col in table.Columns)
            {
                string colName = col.ColumnName.ToLower();

                if (colName.Contains("fee") && !colName.Contains("paid") && !colName.Contains("pending"))
                {
                    if (decimal.TryParse(row[col]?.ToString(), out decimal fee))
                    {
                        totalFee += fee;
                    }
                }
                else if (colName.Contains("paid") && !colName.Contains("pending"))
                {
                    if (decimal.TryParse(row[col]?.ToString(), out decimal paid))
                    {
                        totalPaid += paid;
                    }
                }
            }

            // Update pending column
            var pendingCol = table.Columns.Cast<DataColumn>()
                .FirstOrDefault(c => c.ColumnName.ToLower().Contains("pending") ||
                                   c.ColumnName.ToLower().Contains("balance"));

            if (pendingCol != null)
            {
                row[pendingCol] = Math.Max(0, totalFee - totalPaid);
            }
        }
        private void SavePersistedFiles()
        {
            try
            {
                var paths = _filePaths.Values
                    .Where(p => File.Exists(p))
                    .Distinct()
                    .ToList();

                string json = System.Text.Json.JsonSerializer.Serialize(paths,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistenceFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CsvDataService] SavePersistedFiles failed: {ex.Message}");
            }
        }

        // ── PERSISTENCE: reload previously imported files on startup ─────────
        private void AutoLoadPersistedFiles()
        {
            if (!File.Exists(_persistenceFile)) return;

            try
            {
                string json = File.ReadAllText(_persistenceFile);
                var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (paths == null || paths.Count == 0) return;

                bool anyRemoved = false;
                var validPaths = new List<string>();

                foreach (string path in paths)
                {
                    if (!File.Exists(path))
                    {
                        // File was moved or deleted — drop it from the list
                        System.Diagnostics.Debug.WriteLine(
                            $"[CsvDataService] AutoLoad: skipping missing file: {path}");
                        anyRemoved = true;
                        continue;
                    }

                    try
                    {
                        LoadFileInternal(path);
                        validPaths.Add(path);
                        System.Diagnostics.Debug.WriteLine(
                            $"[CsvDataService] AutoLoad: loaded {path}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[CsvDataService] AutoLoad: failed on {path}: {ex.Message}");
                        anyRemoved = true;
                    }
                }

                // Clean up the JSON if any paths were invalid
                if (anyRemoved)
                {
                    string cleaned = System.Text.Json.JsonSerializer.Serialize(validPaths,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_persistenceFile, cleaned);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CsvDataService] AutoLoadPersistedFiles failed: {ex.Message}");
            }
        }
        // ===========================================
        // SHEET RENAME / STUDENT REMOVE / ARCHIVE
        // ===========================================

        /// <summary>
        /// Assigns a custom display name to a sheet without changing its internal TableName.
        /// The new name is stored in ExtendedProperties["DisplayName"] and is immediately
        /// visible to GetSheetDisplayNames(), FeeCollection, Reports, etc.
        /// Changes are persisted to disk via SaveFile().
        /// </summary>
        public void RenameSheet(string sheetName, string newDisplayName)
        {
            if (string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(newDisplayName))
                return;

            foreach (var dataSet in _loadedFiles.Values)
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (table.TableName == sheetName)
                    {
                        table.ExtendedProperties["DisplayName"] = newDisplayName;
                        // Persist so the custom name survives an app restart
                        SaveFile();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Permanently deletes the given DataRow from its parent DataTable
        /// and writes the change back to disk immediately.
        /// </summary>
        public void RemoveStudentRow(DataRow row)
        {
            if (row == null) return;

            DataTable table = row.Table;
            table.Rows.Remove(row);
            SaveFile();
        }

        /// <summary>
        /// Soft-deletes a student row by setting (or creating) an "Archived" column
        /// to "Yes", then saves to disk.  All other views should filter out rows
        /// where Archived == "Yes".
        /// </summary>
        public void ArchiveStudentRow(DataRow row)
        {
            if (row == null) return;

            DataTable table = row.Table;

            // Create the Archived column on first use if it doesn't exist
            if (!table.Columns.Contains("Archived"))
                table.Columns.Add("Archived", typeof(string));

            row["Archived"] = "Yes";
            SaveFile();
        }

        /// <summary>
        /// Save all changes to files
        /// </summary>
        // Excel rules: max 31 chars; chars \\/\*\?:\[\] are illegal in sheet names.
        private static string SanitizeSheetName(
            string name,
            System.Collections.Generic.HashSet<string> usedNames)
        {
            // Strip illegal characters
            string safeName = name
                .Replace('\\', '_')
                .Replace('/', '_')
                .Replace('*', '_')
                .Replace('?', '_')
                .Replace(':', '_')
                .Replace('[', '_')
                .Replace(']', '_');

            // Truncate to Excel's 31-character hard limit
            if (safeName.Length > 31)
                safeName = safeName.Substring(0, 31);

            if (safeName.Length == 0)
                safeName = "Sheet";

            // Resolve collisions that can arise after truncation
            if (usedNames.Contains(safeName))
            {
                string baseName = safeName.Length > 28 ? safeName.Substring(0, 28) : safeName;
                int counter = 2;
                string candidate;
                do { candidate = baseName + "_" + counter++; }
                while (usedNames.Contains(candidate));
                safeName = candidate;
            }

            return safeName;
        }

        public void SaveFile()
        {
            foreach (var kvp in _loadedFiles)
            {
                string fileKey = kvp.Key;
                DataSet dataSet = kvp.Value;

                // Get file path
                string filePath = _filePaths.ContainsKey(fileKey)
                    ? _filePaths[fileKey]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                  "SchoolFeeData", fileKey);

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // Save to Excel
                using (var workbook = new XLWorkbook())
                {
                    // Track used worksheet names to avoid duplicates after truncation
                    var usedNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                    foreach (DataTable table in dataSet.Tables)
                    {
                        // Excel enforces a hard 31-character limit on worksheet names and
                        // forbids the characters \ / * ? : [ ].  We sanitise here so that
                        // long course names never cause a System.ArgumentException in ClosedXML.
                        string rawName = table.TableName ?? "Sheet";
                        string safeName = SanitizeSheetName(rawName, usedNames);
                        usedNames.Add(safeName);

                        // Clone with the safe name so the in-memory TableName stays unchanged.
                        DataTable clone = table.Copy();
                        clone.TableName = safeName;

                        workbook.Worksheets.Add(clone);
                    }

                    workbook.SaveAs(filePath);
                }
            }
        }
    }
}