using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows; // For MessageBox

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class AttendanceService : IAttendanceService
    {
        private readonly AppDbContext _context;
        private readonly OvertimeCalculationService _overtimeService;

        public AttendanceService(AppDbContext context)
        {
            _context = context;
            _overtimeService = new OvertimeCalculationService(context);
        }

        // =========================================================
        // ASYNC VERSION - PREVENTS UI FREEZE
        // =========================================================
        public async Task ImportAttendanceAsync(string filePath, IProgress<string> progress = null)
        {
            await Task.Run(() => ImportAttendance(filePath, progress));
        }

        // =========================================================
        // SYNCHRONOUS VERSION WITH PROGRESS REPORTING
        // =========================================================
        public void ImportAttendance(string filePath, IProgress<string> progress = null)
        {
            try
            {
                progress?.Report("Reading file...");
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var reader = GetReaderForFile(filePath, stream))
                    {
                        var result = reader.AsDataSet();
                        if (result.Tables.Count == 0) throw new Exception("The file is empty.");

                        var table = result.Tables[0];
                        progress?.Report($"File loaded. Total rows: {table.Rows.Count}");

                        // 1. Check Format
                        // Detect Format
                        string format = DetectFormatOrDie(table);
                        progress?.Report($"Format detected: {format}");

                        // ✅ UPDATED: Add switch case
                        switch (format)
                        {
                            case "FACE_ATTENDANCE":
                                ProcessFaceAttendance(table, progress);
                                break;
                            case "DETAILED_REPORT":
                                ProcessDetailedReport(table, progress);
                                break;
                            case "WORK_DURATION_REPORT":
                                ProcessWorkDurationReport(table, progress);  // ← NEW!
                                break;
                            default:
                                throw new Exception($"Unknown format: {format}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"ERROR: {ex.Message}");
                throw;
            }
        }

        // BACKWARD COMPATIBILITY
        public void ImportAttendance(string filePath)
        {
            ImportAttendance(filePath, null);
        }

        private string DetectFormatOrDie(DataTable table)
        {
            for (int i = 0; i < Math.Min(20, table.Rows.Count); i++)
            {
                string rowStr = string.Join(" ", table.Rows[i].ItemArray.Select(x => x?.ToString() ?? ""));

                // Face Attendance
                if (rowStr.Contains("Code & Name", StringComparison.OrdinalIgnoreCase) ||
                    rowStr.Contains("Total In Time", StringComparison.OrdinalIgnoreCase))
                    return "FACE_ATTENDANCE";

                // Detailed Report (CSV)
                if (rowStr.Contains("Attendance ID", StringComparison.OrdinalIgnoreCase))
                    return "DETAILED_REPORT";

                // ✅ NEW: Work Duration Report
                if (rowStr.Contains("Monthly Status Report", StringComparison.OrdinalIgnoreCase) &&
                    rowStr.Contains("Work Duration", StringComparison.OrdinalIgnoreCase))
                    return "WORK_DURATION_REPORT";

                // Alternative detection
                if (rowStr.Contains("Total Work Duration", StringComparison.OrdinalIgnoreCase) ||
                    rowStr.Contains("WeeklyOff", StringComparison.OrdinalIgnoreCase))
                    return "WORK_DURATION_REPORT";
            }

            // Format not recognized
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("❌ File Format Unknown.");
            sb.AppendLine("\n--- First 5 Rows ---");
            for (int i = 0; i < Math.Min(5, table.Rows.Count); i++)
                sb.AppendLine($"Row {i}: {string.Join("|", table.Rows[i].ItemArray)}");

            throw new Exception(sb.ToString());
        }
        // =========================================================
        // PROCESSOR: WORK DURATION REPORT (.xls FORMAT)
        // =========================================================
        private void ProcessWorkDurationReport(DataTable table, IProgress<string> progress = null)
        {
            var batch = new List<AttendanceRecord>();
            int month = DateTime.Now.Month;
            int year = DateTime.Now.Year;

            progress?.Report("Processing Work Duration Report...");

            // Extract month/year from header
            for (int r = 0; r < Math.Min(10, table.Rows.Count); r++)
            {
                string rowStr = string.Join(" ", table.Rows[r].ItemArray.Select(x => x?.ToString() ?? ""));

                // Pattern: "Jan 01 2026 To Jan 31 2026"
                var dateMatch = Regex.Match(rowStr, @"(\w+)\s+\d{1,2}\s+(\d{4})\s+To\s+\w+\s+\d{1,2}\s+(\d{4})");
                if (dateMatch.Success)
                {
                    string monthName = dateMatch.Groups[1].Value;
                    year = int.Parse(dateMatch.Groups[2].Value);
                    month = DateTime.ParseExact(monthName, "MMM", CultureInfo.InvariantCulture).Month;

                    progress?.Report($"Detected period: {monthName} {year}");
                    break;
                }
            }

            var employeesCache = _context.Employees.ToList();
            var skippedEmployees = new List<(string BioId, string Name, string Format)>(); // Flagged for DB
            int employeesProcessed = 0;
            int recordsProcessed = 0;

            // Find employee blocks
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                string cellValue = row[0]?.ToString()?.Trim() ?? "";

                // ✅ FIX: Check entire row for "Employee:" keyword, not just first cell
                string fullRowText = string.Join(" ", row.ItemArray.Select(x => x?.ToString() ?? "")).Trim();

                // Check if this is an Employee row
                if (cellValue.StartsWith("Employee:", StringComparison.OrdinalIgnoreCase) ||
                    fullRowText.StartsWith("Employee:", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse employee info
                    string fullText = fullRowText;

                    // ✅ FIX: Updated regex to handle both numeric IDs (2078) and alphanumeric IDs (NAT09, NATS01)
                    // Also improved to handle varying name lengths by using .+? instead of [^T]+?
                    var empMatch = Regex.Match(fullText, @"Employee:\s*([A-Za-z0-9]+)\s*:\s*(.+?)(?:\s+Total|$)", RegexOptions.IgnoreCase);
                    if (!empMatch.Success)
                    {
                        progress?.Report($"⚠️ Skipping row {r}: Could not parse employee info from: {fullText.Substring(0, Math.Min(100, fullText.Length))}");
                        continue;
                    }

                    string bioId = empMatch.Groups[1].Value.Trim();
                    string empName = empMatch.Groups[2].Value.Trim();

                    // ✅ FIX: Remove title prefixes like "Ms.", "Mr.", "Mrs."
                    empName = Regex.Replace(empName, @"^(Ms\.|Mr\.|Mrs\.)\s*", "", RegexOptions.IgnoreCase).Trim();

                    progress?.Report($"Processing {empName} (ID: {bioId})...");

                    // ── SS MASTER GUARD ───────────────────────────────────────
                    // Only process employees that already exist in the SS Master.
                    // Never create new employees from the attendance file alone.
                    var emp = FindInSsMaster(employeesCache, bioId, empName, skippedEmployees, "WORK_DURATION_REPORT");
                    if (emp == null)
                    {
                        progress?.Report($"⚠️  Skipped (not in SS Master): {empName} (BioID: {bioId})");
                        r += 8;
                        continue;
                    }

                    employeesProcessed++;

                    // Get data rows (next 4-5 rows after employee row)
                    int statusRow = r + 1;
                    int inTimeRow = r + 2;
                    int outTimeRow = r + 3;
                    int durationRow = r + 4;

                    if (durationRow >= table.Rows.Count)
                    {
                        progress?.Report($"⚠️ Incomplete data for {empName}");
                        continue;
                    }

                    var statusData = table.Rows[statusRow];
                    var inTimeData = table.Rows[inTimeRow];
                    var outTimeData = table.Rows[outTimeRow];
                    var durationData = table.Rows[durationRow];

                    // Process each day (columns 1-31)
                    int daysInMonth = DateTime.DaysInMonth(year, month);

                    for (int day = 1; day <= daysInMonth; day++)
                    {
                        int colIndex = day; // Column 1 = Day 1

                        if (colIndex >= statusData.ItemArray.Length) break;

                        string status = statusData[colIndex]?.ToString()?.Trim() ?? "";
                        string inTime = inTimeData[colIndex]?.ToString()?.Trim() ?? "00:00";
                        string outTime = outTimeData[colIndex]?.ToString()?.Trim() ?? "00:00";
                        string duration = durationData[colIndex]?.ToString()?.Trim() ?? "00:00";

                        // Determine status
                        string finalStatus = "Absent";
                        if (status.Equals("P", StringComparison.OrdinalIgnoreCase))
                            finalStatus = "Present";
                        else if (status.Equals("WO", StringComparison.OrdinalIgnoreCase))
                            finalStatus = "WeeklyOff";
                        else if (status.Equals("A", StringComparison.OrdinalIgnoreCase))
                            finalStatus = "Absent";
                        else if (!string.IsNullOrEmpty(status))
                            finalStatus = status;

                        // Skip absent days and weekly offs
                        if (finalStatus == "Absent" && inTime == "00:00")
                            continue;
                        if (finalStatus == "WeeklyOff")
                            continue;

                        DateTime date = new DateTime(year, month, day);

                        batch.Add(new AttendanceRecord
                        {
                            EmployeeId = emp.Id,
                            Date = date,
                            Status = finalStatus,
                            InTime = inTime,
                            OutTime = outTime,
                            Duration = duration,
                            IsManualEntry = false,
                            Remarks = ""
                        });

                        recordsProcessed++;
                    }

                    // FIX: If this employee had ZERO present days recorded (fully absent month),
                    // save a sentinel "ZERO_ATTENDANCE" record so GenerateDetailedSalary knows
                    // that attendance was checked and the result is genuinely 0 days — not missing data.
                    // Without this, an employee with no records gets calendarDays by mistake.
                    bool hasAnyRecordForThisEmp = batch.Any(b => b.EmployeeId == emp.Id &&
                        b.Date.Month == month && b.Date.Year == year);
                    if (!hasAnyRecordForThisEmp)
                    {
                        // Remove any old sentinel for this employee/month first (idempotent re-import)
                        var oldZero = _context.AttendanceRecords
                            .Where(a => a.EmployeeId == emp.Id &&
                                        a.Date.Month == month && a.Date.Year == year &&
                                        a.Remarks == "ZERO_ATTENDANCE")
                            .ToList();
                        if (oldZero.Any()) _context.AttendanceRecords.RemoveRange(oldZero);

                        batch.Add(new AttendanceRecord
                        {
                            EmployeeId = emp.Id,
                            Date = new DateTime(year, month, 1),
                            Status = "Absent",
                            InTime = "00:00",
                            OutTime = "00:00",
                            Duration = "00:00",
                            IsManualEntry = false,
                            Remarks = "ZERO_ATTENDANCE",   // sentinel: 0 days worked confirmed
                            LateMinutes = 0,
                            OvertimeMinutes = 0,
                            LatePenaltyMinutes = 0
                        });
                        progress?.Report($"ℹ️  {empName}: 0 present days this month (fully absent).");
                    }

                    // Progress update
                    if (employeesProcessed % 10 == 0)
                    {
                        progress?.Report($"Processed {employeesProcessed} employees, {recordsProcessed} records...");
                    }

                    // ✅ FIX: Skip only the 8 data rows, then continue searching for next employee
                    // Each employee block has exactly 8 data rows after the Employee row:
                    // Row r: Employee info
                    // Row r+1: Status
                    // Row r+2: InTime
                    // Row r+3: OutTime
                    // Row r+4: Duration
                    // Row r+5: Late By
                    // Row r+6: Early By
                    // Row r+7: OT
                    // Row r+8: Shift
                    // After that, there may be blank rows, Department rows, or the next Employee
                    // By skipping exactly 8 rows (data rows only), we let the loop naturally
                    // find the next "Employee:" row wherever it appears

                    r += 8; // Skip the 8 data rows; loop will do r++ making total skip = 9
                }
            }

            // Save all records
            if (batch.Any())
            {
                progress?.Report($"Saving {batch.Count} attendance records...");
                AddOrUpdateAttendanceBatch(batch);
            }

            _context.SaveChanges();

            // ── SUMMARY ───────────────────────────────────────────────────────
            string summary = $"✅ Import completed! " +
                $"{employeesProcessed} employees processed, {recordsProcessed} attendance records saved.";
            // Save flagged entries to DB so admin can link them in Staff Directory
            PersistFlaggedEntries(skippedEmployees);

            if (skippedEmployees.Any())
            {
                summary += $"\n\n⚠️  {skippedEmployees.Count} person(s) from the file could not be matched.\n";
                summary += $"   ➡ Open Staff Directory → 'Unmatched Biometrics' tab to link them.\n";
                summary += string.Join("\n", skippedEmployees.Select(s => $"  • {s.Name} (BioID: {s.BioId})"));
            }
            progress?.Report(summary);
        }
        private IExcelDataReader GetReaderForFile(string filePath, FileStream stream)
        {
            if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration()
                {
                    FallbackEncoding = System.Text.Encoding.GetEncoding(1252),
                    AutodetectSeparators = new char[] { ',', ';', '\t' }
                });
            }
            return ExcelReaderFactory.CreateReader(stream);
        }

        // =========================================================
        // PROCESSOR: DETAILED REPORT (CSV FORMAT)
        // =========================================================
        private void ProcessDetailedReport(DataTable table, IProgress<string> progress = null)
        {
            var batch = new List<AttendanceRecord>();
            int month = DateTime.Now.Month;
            int year = DateTime.Now.Year;

            // Extract Month and Year from header
            for (int r = 0; r < 5 && r < table.Rows.Count; r++)
            {
                string rowStr = string.Join(" ", table.Rows[r].ItemArray.Select(x => x?.ToString()));
                var m = Regex.Match(rowStr, @"(\d{1,2})\s*-\s*(\d{4})");
                if (m.Success)
                {
                    month = int.Parse(m.Groups[1].Value);
                    year = int.Parse(m.Groups[2].Value);
                    progress?.Report($"Detected month: {month}/{year}");
                    break;
                }
            }

            // Find Header Row (contains "Attendance ID")
            int headerRow = -1;
            for (int r = 0; r < Math.Min(20, table.Rows.Count); r++)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    if (table.Rows[r][c]?.ToString().Contains("Attendance ID", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        headerRow = r;
                        break;
                    }
                }
                if (headerRow != -1) break;
            }

            if (headerRow == -1)
                throw new Exception("'Attendance ID' header not found in CSV.");

            var employeesCache = _context.Employees.ToList();
            var skippedEmployees = new List<(string BioId, string Name, string Format)>(); // Flagged for DB
            int employeesProcessed = 0;
            int recordsProcessed = 0;

            // Process Rows - Each employee has 3 rows (In-Time, Out-Time, Total-Time)
            for (int i = headerRow + 1; i < table.Rows.Count; i += 3)
            {
                if (i + 2 >= table.Rows.Count) break;

                var inRow = table.Rows[i];
                var outRow = table.Rows[i + 1];
                var totalRow = table.Rows[i + 2];

                // Validate this is an employee block
                string bioId = inRow[0]?.ToString()?.Trim();
                string rawName = inRow[1]?.ToString()?.Trim();
                string inTimeType = inRow[3]?.ToString()?.Trim();

                if (string.IsNullOrEmpty(bioId) || !inTimeType.Contains("In-Time", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Clean employee name (remove parentheses with numbers)
                string cleanName = Regex.Replace(rawName, @"\s*\(\d+\)", "").Trim();

                // ── SS MASTER GUARD ───────────────────────────────────────
                // Only process employees already in the SS Master.
                var emp = FindInSsMaster(employeesCache, bioId, cleanName, skippedEmployees, "DETAILED_REPORT");
                if (emp == null)
                {
                    progress?.Report($"⚠️  Skipped (not in SS Master): {cleanName} (BioID: {bioId})");
                    continue;
                }

                employeesProcessed++;

                // Process each day (columns 4 onwards represent days 1, 2, 3, ... 30/31)
                int daysInMonth = DateTime.DaysInMonth(year, month);
                for (int day = 1; day <= daysInMonth; day++)
                {
                    int colIndex = 3 + day; // Column 4 = Day 1, Column 5 = Day 2, etc.

                    if (colIndex >= inRow.ItemArray.Length) break;

                    string inTime = inRow[colIndex]?.ToString()?.Trim() ?? "0";
                    string outTime = outRow[colIndex]?.ToString()?.Trim() ?? "0";
                    string totalTime = totalRow[colIndex]?.ToString()?.Trim() ?? "0";

                    // Skip if no data for this day
                    if (inTime == "0" || inTime == "00:00" || string.IsNullOrEmpty(inTime))
                        continue;

                    DateTime date = new DateTime(year, month, day);

                    // Determine status
                    string status = "Present";

                    // If out-time is 00:00 or 0, assume missing punch
                    if (outTime == "0" || outTime == "00:00" || string.IsNullOrEmpty(outTime))
                    {
                        outTime = "00:00";
                        status = "MIS"; // Missing out-punch
                    }

                    // Calculate duration if not already provided
                    string duration = "0h 0m";
                    if (!string.IsNullOrEmpty(totalTime) && totalTime != "0")
                    {
                        duration = ConvertTimeStringToDuration(totalTime);
                    }
                    else
                    {
                        duration = CalculateDuration(inTime, outTime);
                    }

                    batch.Add(new AttendanceRecord
                    {
                        EmployeeId = emp.Id,
                        Date = date,
                        Status = status,
                        InTime = inTime,
                        OutTime = outTime,
                        Duration = duration,
                        IsManualEntry = false,
                        Remarks = ""
                    });

                    recordsProcessed++;
                }

                // Report progress
                if (employeesProcessed % 10 == 0)
                {
                    progress?.Report($"Processed {employeesProcessed} employees, {recordsProcessed} records...");
                }
            }

            // Save all records
            if (batch.Any())
            {
                progress?.Report($"Saving {batch.Count} attendance records...");
                AddOrUpdateAttendanceBatch(batch);
            }

            _context.SaveChanges();

            // ── SUMMARY ───────────────────────────────────────────────────────
            string summary = $"✅ Import completed! " +
                $"{employeesProcessed} employees processed, {recordsProcessed} attendance records saved.";
            // Save flagged entries to DB so admin can link them in Staff Directory
            PersistFlaggedEntries(skippedEmployees);

            if (skippedEmployees.Any())
            {
                summary += $"\n\n⚠️  {skippedEmployees.Count} person(s) from the file could not be matched.\n";
                summary += $"   ➡ Open Staff Directory → 'Unmatched Biometrics' tab to link them.\n";
                summary += string.Join("\n", skippedEmployees.Select(s => $"  • {s.Name} (BioID: {s.BioId})"));
            }
            progress?.Report(summary);
        }

        // =========================================================
        // PROCESSOR: FACE ATTENDANCE (EXCEL FORMAT)
        // =========================================================
        private void ProcessFaceAttendance(DataTable table, IProgress<string> progress = null)
        {
            var batch = new List<AttendanceRecord>();
            var tempBatch = new List<AttendanceRecord>();
            Employee currentEmployee = null;
            var skippedEmployees = new List<(string BioId, string Name, string Format)>(); // Flagged for DB

            int employeesProcessed = 0;
            int recordsProcessed = 0;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                string col0 = row[0]?.ToString()?.Trim() ?? "";

                // Skip empty rows
                if (string.IsNullOrWhiteSpace(col0))
                    continue;

                // 1. EMPLOYEE NAME ROW
                if (col0.Contains("Code & Name", StringComparison.OrdinalIgnoreCase))
                {
                    // Save previous employee's records
                    if (tempBatch.Any())
                    {
                        batch.AddRange(tempBatch);
                        tempBatch.Clear();

                        if (batch.Count >= 500)
                        {
                            progress?.Report($"Saving batch... ({batch.Count} records)");
                            AddOrUpdateAttendanceBatch(batch);
                            batch.Clear();
                        }
                    }

                    // Extract employee name - could be in column 1 or column 3 depending on format
                    string fullName = "";

                    // Try column 3 first (newer format: "** Code & Name :- [space] [code] [name]")
                    if (row.ItemArray.Length > 3 && !string.IsNullOrWhiteSpace(row[3]?.ToString()))
                    {
                        fullName = row[3].ToString().Trim();
                    }
                    // Fall back to column 1 (older format: "** Code & Name :- [name]")
                    else if (row.ItemArray.Length > 1 && !string.IsNullOrWhiteSpace(row[1]?.ToString()))
                    {
                        fullName = row[1].ToString().Trim();
                    }

                    if (string.IsNullOrEmpty(fullName)) continue;

                    // Extract BiometricId if available (column 2)
                    string bioId = row.ItemArray.Length > 2 ? row[2]?.ToString()?.Trim() : "";

                    var employees = _context.Employees.ToList();

                    // ── SS MASTER GUARD ───────────────────────────────────────
                    // Only process employees already in the SS Master.
                    currentEmployee = FindInSsMaster(employees, bioId, fullName, skippedEmployees, "FACE_ATTENDANCE");
                    if (currentEmployee == null)
                    {
                        progress?.Report($"⚠️  Skipped (not in SS Master): {fullName} (BioID: {bioId})");
                        continue;
                    }

                    employeesProcessed++;
                    progress?.Report($"Processing: {fullName}");

                    continue;
                }

                // 2. ATTENDANCE ROW PROCESSING
                if (currentEmployee != null && !string.IsNullOrEmpty(col0) && col0.Length > 0 && char.IsDigit(col0[0]))
                {
                    if (DateTime.TryParseExact(col0, "dd/MM/yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        if (currentEmployee.Id == 0)
                        {
                            throw new Exception($"Employee {currentEmployee.FirstName} has no ID!");
                        }

                        string totalInTime = (row.ItemArray.Length > 2 && row[2] != null)
                            ? row[2].ToString()?.Trim() : "00:00";

                        string totalOutTime = (row.ItemArray.Length > 3 && row[3] != null)
                            ? row[3].ToString()?.Trim() : "00:00";

                        string status = (row.ItemArray.Length > 4 && row[4] != null)
                            ? row[4].ToString()?.Trim() : "";

                        string punches = (row.ItemArray.Length > 5 && row[5] != null)
                            ? row[5].ToString()?.Trim() : "";

                        // FIX 1: Skip Weekly Off days entirely — they are not holidays,
                        // not absent, just non-working days. Don't save them.
                        if (status == "WO" || status == "OFF")
                            continue;

                        string finalStatus = "Present";

                        if (status == "A")
                        {
                            finalStatus = "Absent";
                        }
                        else if (status == "MIS")
                        {
                            finalStatus = "MIS";
                        }

                        string inTime = "00:00";
                        string outTime = "00:00";

                        // FIX 2: Extract from punches column FIRST (most accurate source)
                        // The punches column has the real(I) and (O) times
                        if (!string.IsNullOrEmpty(punches))
                        {
                            var inMatch = Regex.Match(punches, @"(\d{2}:\d{2})\(I\)");
                            if (inMatch.Success)
                                inTime = inMatch.Groups[1].Value;

                            var outMatch = Regex.Match(punches, @"(\d{2}:\d{2})\(O\)");
                            if (outMatch.Success)
                                outTime = outMatch.Groups[1].Value;
                        }

                        // Fall back to totalInTime/totalOutTime columns if punches had nothing
                        if (inTime == "00:00" && !string.IsNullOrEmpty(totalInTime) && totalInTime != "00:00")
                            inTime = totalInTime;

                        if (outTime == "00:00" && !string.IsNullOrEmpty(totalOutTime) && totalOutTime != "00:00")
                            outTime = totalOutTime;

                        // FIX 3: Apply MIS rule AFTER punch extraction so it only fires
                        // when there is genuinely no (O) punch — not overwritten by punch regex
                        // MIS = forgot OUT punch → auto mark 5:30 PM
                        if (status == "MIS" && inTime != "00:00" && outTime == "00:00")
                        {
                            outTime = "17:30";
                        }

                        // Mark as Present if we have a valid in-time
                        if (inTime != "00:00")
                            finalStatus = "Present";

                        tempBatch.Add(new AttendanceRecord
                        {
                            EmployeeId = currentEmployee.Id,
                            Date = date,
                            Status = finalStatus,
                            InTime = inTime,
                            OutTime = outTime,
                            Duration = CalculateDuration(inTime, outTime)
                        });

                        recordsProcessed++;
                    }
                }

                // Report progress every 100 rows
                if (i % 100 == 0)
                {
                    progress?.Report($"Processed {i}/{table.Rows.Count} rows... ({employeesProcessed} employees, {recordsProcessed} records)");
                }
            }

            // Save remaining records
            if (tempBatch.Any())
            {
                batch.AddRange(tempBatch);
            }

            if (batch.Any())
            {
                progress?.Report($"Saving final batch... ({batch.Count} records)");
                AddOrUpdateAttendanceBatch(batch);
            }

            _context.SaveChanges();

            // ── SUMMARY ───────────────────────────────────────────────────────
            string faSummary = $"✅ Import completed! " +
                $"{employeesProcessed} employees processed, {recordsProcessed} attendance records saved.";
            // Save flagged entries to DB so admin can link them in Staff Directory
            PersistFlaggedEntries(skippedEmployees);

            if (skippedEmployees.Any())
            {
                faSummary += $"\n\n⚠️  {skippedEmployees.Count} person(s) from the file could not be matched.\n";
                faSummary += $"   ➡ Open Staff Directory → 'Unmatched Biometrics' tab to link them.\n";
                faSummary += string.Join("\n", skippedEmployees.Select(s => $"  • {s.Name} (BioID: {s.BioId})"));
            }
            progress?.Report(faSummary);
        }

        // =========================================================
        // BATCH SAVE HELPER
        // =========================================================

        private void AddOrUpdateAttendanceBatch(List<AttendanceRecord> newRecords)
        {
            if (!newRecords.Any()) return;

            foreach (var r in newRecords)
            {
                r.Date = r.Date.Date;

                if (r.EmployeeId == 0)
                {
                    throw new Exception($"Invalid EmployeeId=0 for date {r.Date:yyyy-MM-dd}");
                }
            }

            var empIds = newRecords.Select(r => r.EmployeeId).Distinct().ToList();
            var dates = newRecords.Select(r => r.Date).Distinct().ToList();

            var existingRecords = _context.AttendanceRecords
                .Where(r => empIds.Contains(r.EmployeeId) && dates.Contains(r.Date))
                .ToList();

            foreach (var newRecord in newRecords)
            {
                var existing = existingRecords.FirstOrDefault(r =>
                    r.EmployeeId == newRecord.EmployeeId && r.Date == newRecord.Date);

                if (existing != null)
                {
                    existing.InTime = newRecord.InTime;
                    existing.OutTime = newRecord.OutTime;
                    existing.Duration = newRecord.Duration;
                    existing.Status = newRecord.Status;

                    // 🆕 CALCULATE OVERTIME & PENALTIES
                    _overtimeService.CalculateOvertimeAndPenalties(existing);
                }
                else
                {
                    _context.AttendanceRecords.Add(newRecord);
                    _context.SaveChanges(); // Save to get ID

                    // 🆕 CALCULATE OVERTIME & PENALTIES
                    _overtimeService.CalculateOvertimeAndPenalties(newRecord);
                }
            }

            _context.SaveChanges();
        }

        // =========================================================
        // HELPER FUNCTIONS
        // =========================================================
        private string CalculateDuration(string inTime, string outTime)
        {
            if (TimeSpan.TryParse(inTime, out var t1) && TimeSpan.TryParse(outTime, out var t2))
            {
                if (t2 == TimeSpan.Zero) return "0h 0m";
                var diff = t2 - t1;
                if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromHours(24));
                return $"{(int)diff.TotalHours}h {diff.Minutes}m";
            }
            return "0h 0m";
        }

        /// <summary>
        /// Converts time strings like "07:50" to duration format "7h 50m"
        /// </summary>
        private string ConvertTimeStringToDuration(string timeStr)
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr == "0") return "0h 0m";

            if (TimeSpan.TryParse(timeStr, out var time))
            {
                return $"{(int)time.TotalHours}h {time.Minutes}m";
            }
            return "0h 0m";
        }

        // =========================================================
        // INTERFACE METHODS
        // =========================================================
        public List<AttendanceRecord> GetAttendance(int month, int year, int? employeeId = null)
        {
            var query = _context.AttendanceRecords.Include(a => a.Employee).AsQueryable();
            if (employeeId.HasValue && employeeId > 0) query = query.Where(a => a.EmployeeId == employeeId);
            if (month > 0) query = query.Where(a => a.Date.Month == month && a.Date.Year == year);
            return query.OrderByDescending(a => a.Date).ToList();
        }
        // =========================================================
        // ⚠️ DEV ONLY - RESET ALL ATTENDANCE & ALLOWANCE DATA
        // This method is for internal use only. Hide the button before giving to company.
        // =========================================================
        public void ResetAllAttendanceAndAllowances(IProgress<string> progress = null)
        {
            progress?.Report("Deleting all attendance records...");
            var allRecords = _context.AttendanceRecords.ToList();
            _context.AttendanceRecords.RemoveRange(allRecords);
            _context.SaveChanges();
            progress?.Report($"Deleted {allRecords.Count} attendance records.");

            progress?.Report("Resetting all overtime allowance banks...");
            var allAllowances = _context.OvertimeAllowances.ToList();
            foreach (var a in allAllowances)
            {
                a.TotalAllowanceMinutes = 0;
                a.UsedAllowanceMinutes = 0;
                a.LastUpdated = DateTime.Now;
            }
            _context.SaveChanges();
            progress?.Report($"Reset {allAllowances.Count} allowance banks.");

            progress?.Report("✅ Reset complete. You can now re-import attendance files.");
        }

        public void MarkAttendance(AttendanceRecord record) => AddOrUpdateAttendanceBatch(new List<AttendanceRecord> { record });
        public void BulkMarkAttendance(List<AttendanceRecord> records) => AddOrUpdateAttendanceBatch(records);
        public void AddHoliday(Holiday holiday) { if (!_context.Holidays.Any(h => h.Date == holiday.Date)) { _context.Holidays.Add(holiday); _context.SaveChanges(); } }
        public void DeleteHoliday(int id) { var item = _context.Holidays.Find(id); if (item != null) { _context.Holidays.Remove(item); _context.SaveChanges(); } }
        public List<Holiday> GetHolidays(int year) => _context.Holidays.Where(h => h.Date.Year == year).OrderBy(h => h.Date).ToList();
        public void UpdateRecord(AttendanceRecord record) => MarkAttendance(record);
        public void ImportHolidays(string filePath) { }
        public void ImportBiometricReport(string filePath) => ImportAttendance(filePath);
        public void AddAttendanceRecord(AttendanceRecord record) => MarkAttendance(record);
        IEnumerable<AttendanceRecord> IAttendanceService.GetRecords(int id, int month, int year) => GetAttendance(month, year, id);
        // =========================================================
        // SS MASTER GUARD — Find employee or flag for manual linking
        // =========================================================
        /// <summary>
        /// Matches an attendance-file person to an SS Master employee using 4 tiers:
        ///
        ///   TIER 1 — BiometricId exact match. Zero ambiguity. Used after the first
        ///            successful import has back-filled the ID into the DB.
        ///
        ///   TIER 2 — Exact normalised name match (unique result only).
        ///            "Mr. Ashish Kumar" → "ASHISH KUMAR", "MRS RITU GOYAL" → "RITU GOYAL".
        ///            If 2+ employees share the name → jump straight to Unmatched tab.
        ///            On success, BiometricId is back-filled so next import uses Tier 1.
        ///
        ///   TIER 3 — Safe fuzzy name match (unique result only). Handles real-world
        ///            spelling differences between the biometric machine and SS Master:
        ///              • Space difference   : "RAJ DYAL" ↔ "RAJDYAL"
        ///              • Missing surname    : "RUBAL" ↔ "RUBAL MASIH" (≥4 chars prefix)
        ///              • Extra middle name  : "RAJESH DUBEY" ↔ "RAJESH KUMAR DUBEY"
        ///              • 1-char typo only for long names (≥10 compact chars):
        ///                  "SUNIL YADEV" ↔ "SUNIL YADAV", "MANPREET SINGH GHUMMAN" ↔ "…GHUMAN"
        ///            Levenshtein is intentionally restricted to long names to prevent
        ///            false matches like BABY→BABLU, HARPREET→GURPREET, RAJ KUMAR→SURAJ KUMAR.
        ///            If fuzzy produces 2+ candidates → Unmatched tab.
        ///
        ///   UNMATCHED — Genuinely not in SS Master → flagged for manual linking.
        /// </summary>
        private Employee FindInSsMaster(
            List<Employee> ssMasterCache,
            string bioId,
            string fullName,
            List<(string BioId, string Name, string Format)> skippedLog,
            string sourceFormat = "")
        {
            Employee emp = null;
            bool fileHasBioId = !string.IsNullOrWhiteSpace(bioId);

            // ── TIER 1: Exact BiometricId match ───────────────────────────────
            if (fileHasBioId)
            {
                emp = ssMasterCache.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e.BiometricId) &&
                    string.Equals(e.BiometricId.Trim(), bioId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            // ── TIER 2: Exact normalised name match ───────────────────────────
            if (emp == null && !string.IsNullOrWhiteSpace(fullName))
            {
                string normInput = NormaliseName(fullName);
                var exactMatches = ssMasterCache
                    .Where(e => string.Equals(NormaliseName(e.FullName), normInput, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (exactMatches.Count == 1)
                {
                    emp = exactMatches[0];
                }
                else if (exactMatches.Count > 1)
                {
                    // Ambiguous exact name → flag immediately, don't try fuzzy
                    LogSkipped(skippedLog, bioId, fullName, sourceFormat);
                    return null;
                }
            }

            // ── TIER 3: Safe fuzzy name match ─────────────────────────────────
            if (emp == null && !string.IsNullOrWhiteSpace(fullName))
            {
                string normInput = NormaliseName(fullName);
                string compactInput = normInput.Replace(" ", "");

                var fuzzyMatches = ssMasterCache.Where(e =>
                {
                    string normDb = NormaliseName(e.FullName);
                    string compactDb = normDb.Replace(" ", "");

                    // Rule A: Space-only difference ("RAJ DYAL" ↔ "RAJDYAL")
                    if (string.Equals(compactInput, compactDb, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // Rule B: One compact string is a prefix of the other.
                    //         Minimum 5 chars on the shorter side to avoid over-matching
                    //         short common names (e.g. "RAM" shouldn't match "RAMESH KUMAR").
                    //         Both sides must also share at least 75% of the longer length
                    //         so "AMANPREETSINGH" does NOT match "MANPREETSINGH".
                    int minLen = Math.Min(compactInput.Length, compactDb.Length);
                    int maxLen = Math.Max(compactInput.Length, compactDb.Length);
                    if (minLen >= 5 && (double)minLen / maxLen >= 0.75)
                    {
                        if (compactDb.StartsWith(compactInput, StringComparison.OrdinalIgnoreCase) ||
                            compactInput.StartsWith(compactDb, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    // Rule C: Word-subset — all words of the shorter name appear in the
                    //         longer name (e.g. "RAJESH DUBEY" ↔ "RAJESH KUMAR DUBEY").
                    //         Requires at least 2 words to avoid over-matching.
                    var inputWords = normInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var dbWords = normDb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (inputWords.Length >= 2 && dbWords.Length >= 2)
                    {
                        var shorter = inputWords.Length <= dbWords.Length ? inputWords : dbWords;
                        var longer = inputWords.Length <= dbWords.Length ? dbWords : inputWords;
                        if (shorter.All(w => longer.Any(lw =>
                            string.Equals(lw, w, StringComparison.OrdinalIgnoreCase))))
                            return true;
                    }

                    // Rule D: 1-char typo, but ONLY for long names (compact ≥ 10 chars)
                    //         AND first word must match (so "AMANPREET" ≠ "MANPREET")
                    if (compactInput.Length >= 10 && compactDb.Length >= 10 &&
                        Math.Abs(compactInput.Length - compactDb.Length) <= 1)
                    {
                        // Guard: first words must be the same to prevent cross-name typos
                        var firstWordInput = normInput.Split(' ')[0];
                        var firstWordDb = normDb.Split(' ')[0];
                        bool firstWordMatch = string.Equals(firstWordInput, firstWordDb,
                            StringComparison.OrdinalIgnoreCase);
                        if (firstWordMatch && LevenshteinDistance(compactInput, compactDb) == 1)
                            return true;
                    }

                    return false;
                }).ToList();

                if (fuzzyMatches.Count == 1)
                {
                    emp = fuzzyMatches[0];
                    System.Diagnostics.Debug.WriteLine(
                        $"[AttendanceService] Fuzzy matched '{fullName}' → '{emp.FullName}'");
                }
                // If fuzzyMatches.Count > 1 → too ambiguous, emp stays null → Unmatched tab
            }

            // ── Back-fill BiometricId OR detect conflict ───────────────────────
            // If the employee was matched by name but already has a DIFFERENT BiometricId
            // stored in the DB, we have a conflict: two attendance-file people with
            // different codes both name-matched to the same employee record.
            // Classic case: two "Harjit Singh" — BioID 102 was matched first and
            // written to the DB; when BioID CIHT87 arrives, it must NOT silently
            // overwrite 102 or merge attendance into the same record.
            // Instead: flag BioID CIHT87 for manual resolution (Add as New Employee
            // or Link to the correct record via the Unmatched tab).
            if (emp != null && fileHasBioId)
            {
                bool dbHasDifferentBioId = !string.IsNullOrWhiteSpace(emp.BiometricId) &&
                    !string.Equals(emp.BiometricId.Trim(), bioId.Trim(), StringComparison.OrdinalIgnoreCase);

                if (dbHasDifferentBioId)
                {
                    // Conflict: this attendance entry has a code that doesn't match the
                    // code already stored for this employee. Flag it and don't import.
                    System.Diagnostics.Debug.WriteLine(
                        $"[AttendanceService] BioID conflict: file has '{bioId}' for '{fullName}' " +
                        $"but DB employee already has BioID '{emp.BiometricId}'. Flagging.");
                    LogSkipped(skippedLog, bioId, fullName, sourceFormat);
                    return null;
                }

                // Employee has no BiometricId yet — safe to back-fill
                if (string.IsNullOrWhiteSpace(emp.BiometricId))
                {
                    emp.BiometricId = bioId.Trim();
                    _context.Employees.Update(emp);
                    // SaveChanges batched at end of import
                }
            }

            // ── Not found → log for Unmatched Biometrics tab ─────────────────
            if (emp == null)
                LogSkipped(skippedLog, bioId, fullName, sourceFormat);

            return emp;
        }

        private static void LogSkipped(
            List<(string BioId, string Name, string Format)> log,
            string bioId, string name, string format)
        {
            if (string.IsNullOrWhiteSpace(bioId) && string.IsNullOrWhiteSpace(name)) return;
            bool already = log.Any(s =>
                string.Equals(s.BioId, bioId ?? "", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Name, name ?? "", StringComparison.OrdinalIgnoreCase));
            if (!already) log.Add((bioId ?? "", name ?? "", format ?? ""));
        }

        /// <summary>Levenshtein edit distance. Only called on long-name pairs (≥10 compact chars).</summary>
        private static int LevenshteinDistance(string a, string b)
        {
            int m = a.Length, n = b.Length;
            var d = new int[m + 1, n + 1];
            for (int i = 0; i <= m; i++) d[i, 0] = i;
            for (int j = 0; j <= n; j++) d[0, j] = j;
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + (char.ToUpper(a[i - 1]) == char.ToUpper(b[j - 1]) ? 0 : 1));
            return d[m, n];
        }

        // =========================================================
        // PERSIST FLAGGED ENTRIES TO DB
        // =========================================================
        /// <summary>
        /// Upserts unmatched biometric entries to FlaggedBiometricEntries.
        /// Skips any BioId already in the flagged table or already assigned to a real employee.
        /// </summary>
        private void PersistFlaggedEntries(List<(string BioId, string Name, string Format)> entries)
        {
            if (!entries.Any()) return;

            var existingBioIds = _context.FlaggedBiometricEntries
                .Select(f => f.BiometricId.ToLower())
                .ToHashSet();

            var assignedBioIds = _context.Employees
                .Where(e => e.BiometricId != null && e.BiometricId != "")
                .Select(e => e.BiometricId.ToLower())
                .ToHashSet();

            var toAdd = new List<FlaggedBiometricEntry>();
            foreach (var (bioId, name, format) in entries)
            {
                string key = (bioId ?? "").ToLower();
                if (!string.IsNullOrEmpty(key) && existingBioIds.Contains(key)) continue;
                if (!string.IsNullOrEmpty(key) && assignedBioIds.Contains(key)) continue;

                toAdd.Add(new FlaggedBiometricEntry
                {
                    BiometricId = bioId ?? "",
                    BiometricName = name ?? "",
                    SourceFormat = format ?? "",
                    IsResolved = false,
                    FirstSeenOn = DateTime.Now
                });
            }

            if (toAdd.Any())
            {
                _context.FlaggedBiometricEntries.AddRange(toAdd);
                _context.SaveChanges();
            }
        }

        /// <summary>
        /// Strips name prefixes (Mr/Mrs/Ms with or without dot, with or without trailing space)
        /// and normalises whitespace to single spaces, uppercased.
        /// "Mr. Ashish Kumar" → "ASHISH KUMAR"
        /// "MRS RITU GOYAL"   → "RITU GOYAL"
        /// "Mr.Anuj"          → "ANUJ"
        /// </summary>
        private static string NormaliseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            // Strip title prefix with or without dot and with or without space after
            string stripped = Regex.Replace(name.Trim(),
                @"^(Mr\.?|Mrs\.?|Ms\.?|MR\.?|MRS\.?|MS\.?|Dr\.?)\s*",
                "", RegexOptions.IgnoreCase);
            // Collapse multiple spaces and uppercase
            return Regex.Replace(stripped.Trim(), @"\s+", " ").ToUpperInvariant();
        }


    }
}