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

        public AttendanceService(AppDbContext context)
        {
            _context = context;
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
                        string format = DetectFormatOrDie(table);
                        progress?.Report($"Format detected: {format}");

                        if (format == "FACE_ATTENDANCE")
                        {
                            ProcessFaceAttendance(table, progress);
                        }
                        else
                        {
                            ProcessDetailedReport(table, progress);
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

                if (rowStr.Contains("Code & Name", StringComparison.OrdinalIgnoreCase) ||
                    rowStr.Contains("Total In Time", StringComparison.OrdinalIgnoreCase) ||
                    rowStr.Contains("In/Out Punches", StringComparison.OrdinalIgnoreCase))
                    return "FACE_ATTENDANCE";

                if (rowStr.Contains("Attendance ID", StringComparison.OrdinalIgnoreCase))
                    return "DETAILED_REPORT";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("❌ File Format Unknown.");
            sb.AppendLine("\n--- First 5 Rows ---");
            for (int i = 0; i < Math.Min(5, table.Rows.Count); i++)
                sb.AppendLine($"Row {i}: {string.Join("|", table.Rows[i].ItemArray)}");

            throw new Exception(sb.ToString());
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

                // Find or create employee
                var emp = employeesCache.FirstOrDefault(e => e.BiometricId == bioId);
                if (emp == null)
                {
                    emp = employeesCache.FirstOrDefault(e => e.FullName.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                    if (emp != null)
                    {
                        emp.BiometricId = bioId;
                        _context.Employees.Update(emp);
                    }
                }

                // Create new employee if not found
                if (emp == null)
                {
                    string[] parts = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    emp = new Employee
                    {
                        FirstName = parts.Length > 0 ? parts[0] : cleanName,
                        LastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "",
                        BiometricId = bioId,
                        Designation = inRow[2]?.ToString()?.Trim() ?? "Staff"
                    };

                    _context.Employees.Add(emp);
                    _context.SaveChanges(); // Save immediately to get the ID
                    employeesCache.Add(emp); // Add to cache

                    progress?.Report($"✨ Created new employee: {cleanName} (BioID: {bioId})");
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
            progress?.Report($"✅ Import completed! {employeesProcessed} employees, {recordsProcessed} attendance records.");
        }

        // =========================================================
        // PROCESSOR: FACE ATTENDANCE (EXCEL FORMAT)
        // =========================================================
        private void ProcessFaceAttendance(DataTable table, IProgress<string> progress = null)
        {
            var batch = new List<AttendanceRecord>();
            var tempBatch = new List<AttendanceRecord>();
            Employee currentEmployee = null;

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

                    // Try to find employee by BiometricId first, then by name
                    currentEmployee = null;
                    if (!string.IsNullOrEmpty(bioId))
                    {
                        currentEmployee = employees.FirstOrDefault(e => e.BiometricId == bioId);
                    }

                    if (currentEmployee == null)
                    {
                        currentEmployee = employees.FirstOrDefault(e =>
                            e.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (currentEmployee == null)
                    {
                        string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        currentEmployee = new Employee
                        {
                            FirstName = parts.Length > 0 ? parts[0] : fullName,
                            LastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "",
                            BiometricId = bioId
                        };

                        _context.Employees.Add(currentEmployee);
                        _context.SaveChanges();
                    }
                    else if (string.IsNullOrEmpty(currentEmployee.BiometricId) && !string.IsNullOrEmpty(bioId))
                    {
                        // Update BiometricId if employee exists but doesn't have one
                        currentEmployee.BiometricId = bioId;
                        _context.Employees.Update(currentEmployee);
                        _context.SaveChanges();
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

                        string finalStatus = "Present";

                        if (status == "WO" || status == "OFF")
                        {
                            finalStatus = "Holiday";
                        }
                        else if (status == "A")
                        {
                            finalStatus = "Absent";
                        }
                        else if (status == "MIS")
                        {
                            finalStatus = "MIS";   // keep MIS
                        }


                        string inTime = "00:00";
                        string outTime = "00:00";

                        if (!string.IsNullOrEmpty(totalInTime) && totalInTime != "00:00")
                            inTime = totalInTime;

                        if (!string.IsNullOrEmpty(totalOutTime) && totalOutTime != "00:00")
                            outTime = totalOutTime;
                        // BUSINESS RULE:
                        // MIS = forgot OUT punch → auto mark 5:00 PM
                        if (status == "MIS" && inTime != "00:00")
                        {
                            outTime = "17:00";
                        }


                        if (!string.IsNullOrEmpty(punches))
                        {
                            var inMatch = Regex.Match(punches, @"(\d{2}:\d{2})\(I\)");
                            if (inMatch.Success)
                                inTime = inMatch.Groups[1].Value;

                            var outMatch = Regex.Match(punches, @"(\d{2}:\d{2})\(O\)");
                            if (outMatch.Success)
                                outTime = outMatch.Groups[1].Value;
                        }

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

            progress?.Report($"Import completed! {employeesProcessed} employees, {recordsProcessed} attendance records.");
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
                }
                else
                {
                    _context.AttendanceRecords.Add(newRecord);
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
    }
}