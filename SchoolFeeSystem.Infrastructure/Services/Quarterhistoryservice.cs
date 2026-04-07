using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SchoolFeeSystem.Presentation.Services
{
    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterHistoryService  (Fixed)
    //
    //  ROOT CAUSE of the "history disappears after quarter transition" bug:
    //  ────────────────────────────────────────────────────────────────────────
    //  The old SheetKey was  DEPT|YEAR|SEM  (e.g. "MECHATRONICS|1|3").
    //  When AcademicCycleService promotes a class to the next semester
    //  (Sem 3 → Sem 4) the key changes to "MECHATRONICS|1|4", so all
    //  snapshots filed under "MECHATRONICS|1|3" become invisible.
    //
    //  Fix: SheetKey is now  DEPT|YEAR  (e.g. "MECHATRONICS|1").
    //  The Year portion only changes once per academic year (after May-Jul),
    //  so every snapshot for a class that spans two semesters within one year
    //  is grouped under the same key.  The semester is stored PER ENTRY in
    //  HistoryEntry.Semester so the UI can still show "Sem 3 / Sem 4".
    //
    //  Additional fix: SnapshotCurrentQuarter() is a new public method that
    //  AcademicCycleService and the UI can call at any time to save the live
    //  state without advancing to the next quarter.  This lets the Feb-Apr 2026
    //  snapshot be written as soon as May arrives (or on demand).
    // ══════════════════════════════════════════════════════════════════════════

    public class QuarterHistoryService
    {
        // ── Storage root ────────────────────────────────────────────────────
        private readonly string _historyDir;

        public QuarterHistoryService()
        {
            _historyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem", "history");
            Directory.CreateDirectory(_historyDir);
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Call this just BEFORE a sheet is advanced to the next quarter.
        /// Stores a permanent copy of the completed-quarter data.
        /// </summary>
        public void Snapshot(DataTable sheet, string quarter, int calendarYear,
                             DateTime originalFileAddedDate)
        {
            try
            {
                // KEY FIX: use the Dept|Year key (stable across semester bumps)
                string key = SheetKey(sheet);
                var index = LoadIndex(key);

                // Avoid double-snapshotting the same quarter
                if (index.Any(e => e.Quarter == quarter && e.CalendarYear == calendarYear))
                    return;

                string dataFile = DataFilePath(key, quarter, calendarYear);
                WriteCsv(sheet, dataFile);

                int sem  = GetSemester(sheet);
                int yr   = GetYear(sheet);

                index.Add(new HistoryEntry
                {
                    Quarter             = quarter,
                    CalendarYear        = calendarYear,
                    Semester            = sem,
                    AcademicYear        = yr,
                    Period              = sheet.ExtendedProperties["Period"]?.ToString()     ?? "",
                    CourseInfo          = sheet.ExtendedProperties["CourseInfo"]?.ToString() ?? "",
                    Department          = sheet.ExtendedProperties["Department"]?.ToString() ?? "",
                    DataFile            = dataFile,
                    OriginalFileAdded   = originalFileAddedDate,
                    SnapshotTaken       = DateTime.Now,
                    TotalStudents       = CountStudents(sheet),
                    PaidStudents        = CountPaid(sheet),
                });

                SaveIndex(key, index);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[History] Snapshot failed for {sheet.TableName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves a snapshot of the CURRENT live quarter without advancing it.
        /// Safe to call multiple times — will not create duplicate entries.
        /// Call this: (a) on first file import, (b) at app startup, so the current
        /// quarter is always visible in the history panel even before a transition.
        /// </summary>
        public void SnapshotCurrentQuarter(DataTable sheet, DateTime? importedOn = null)
        {
            string q  = sheet.ExtendedProperties["Quarter"]?.ToString()
                        ?? AcademicCycleService.CurrentQuarter();
            int    yr = CalendarYearForQuarter(q, DateTime.Now);
            Snapshot(sheet, q, yr, importedOn ?? DateTime.Now);
        }

        /// <summary>
        /// Called when a file is first imported so Q0 appears in the timeline.
        /// </summary>
        public void RecordImport(DataTable sheet, DateTime importedOn)
            => SnapshotCurrentQuarter(sheet, importedOn);

        /// <summary>
        /// Returns the history entries for a sheet, sorted oldest → newest.
        /// Uses the stable Dept|Year key so entries survive semester promotions.
        /// </summary>
        public List<HistoryEntry> GetHistory(DataTable sheet)
            => GetHistory(SheetKey(sheet));

        /// <summary>
        /// Returns the history entries by sheet key.
        /// </summary>
        public List<HistoryEntry> GetHistory(string sheetKey)
        {
            var index = LoadIndex(sheetKey);
            return index.OrderBy(e => AcademicOrder(e.Quarter, e.CalendarYear)).ToList();
        }

        /// <summary>
        /// Loads the snapshot DataTable for a given history entry.
        /// Returns null if the data file is missing.
        /// </summary>
        public DataTable LoadSnapshot(HistoryEntry entry)
        {
            try
            {
                if (!File.Exists(entry.DataFile)) return null;
                return ReadCsv(entry.DataFile, entry.Quarter, entry.Period,
                               entry.Department, entry.AcademicYear, entry.Semester);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[History] LoadSnapshot failed: {ex.Message}");
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SHEET KEY  ← THE CRITICAL FIX
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Stable identifier: DEPT|YEAR — groups ALL quarters of one class
        /// across semester boundaries within the same academic year.
        ///
        /// e.g. Mechatronics Year 1 → "MECHATRONICS|1"
        ///   Sem 3 (Feb-Apr) and Sem 4 (May-Jul) both hash to the same key.
        ///
        /// Year only increments after May-Jul, so all four quarters of an
        /// academic year share one key.
        /// </summary>
        public static string SheetKey(DataTable sheet)
        {
            string dept = sheet.ExtendedProperties["Department"]?.ToString() ?? "UNK";
            string yr   = sheet.ExtendedProperties["Year"]?.ToString()       ?? "0";
            return $"{dept}|{yr}";
        }

        /// <summary>
        /// Overload for callers that already know the department and year
        /// (e.g. after a transition when the sheet name has changed).
        /// </summary>
        public static string SheetKey(string department, int academicYear)
            => $"{department}|{academicYear}";

        // ════════════════════════════════════════════════════════════════════
        // INDEX PERSISTENCE
        // ════════════════════════════════════════════════════════════════════

        private string IndexFilePath(string key) =>
            Path.Combine(_historyDir, SanitiseFilename(key) + ".json");

        private string DataFilePath(string key, string quarter, int calYear) =>
            Path.Combine(_historyDir,
                $"{SanitiseFilename(key)}_{quarter.Replace("-", "")}_{calYear}.csv");

        private List<HistoryEntry> LoadIndex(string key)
        {
            string path = IndexFilePath(key);
            if (!File.Exists(path)) return new List<HistoryEntry>();
            try
            {
                return JsonSerializer.Deserialize<List<HistoryEntry>>(
                    File.ReadAllText(path)) ?? new List<HistoryEntry>();
            }
            catch { return new List<HistoryEntry>(); }
        }

        private void SaveIndex(string key, List<HistoryEntry> index)
        {
            File.WriteAllText(IndexFilePath(key),
                JsonSerializer.Serialize(index,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        // ════════════════════════════════════════════════════════════════════
        // CSV SERIALISATION
        // ════════════════════════════════════════════════════════════════════

        private static void WriteCsv(DataTable table, string path)
        {
            using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            var cols = table.Columns.Cast<DataColumn>().ToList();
            sw.WriteLine(string.Join("\t", cols.Select(c => EscapeTab(c.ColumnName))));
            foreach (DataRow row in table.Rows)
                sw.WriteLine(string.Join("\t",
                    cols.Select(c => EscapeTab(row[c]?.ToString() ?? ""))));
        }

        private static DataTable ReadCsv(string path, string quarter, string period,
                                          string dept, int year, int sem)
        {
            var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            if (lines.Length == 0) return new DataTable();

            var table   = new DataTable();
            var headers = lines[0].Split('\t').Select(UnescapeTab).ToArray();
            foreach (var h in headers)
                table.Columns.Add(h);

            for (int i = 1; i < lines.Length; i++)
            {
                var cells = lines[i].Split('\t').Select(UnescapeTab).ToArray();
                var row   = table.NewRow();
                for (int c = 0; c < headers.Length && c < cells.Length; c++)
                    row[c] = cells[c];
                table.Rows.Add(row);
            }

            table.ExtendedProperties["Quarter"]    = quarter;
            table.ExtendedProperties["Period"]     = period;
            table.ExtendedProperties["Department"] = dept;
            table.ExtendedProperties["Year"]       = year.ToString();
            table.ExtendedProperties["Semester"]   = sem.ToString();

            return table;
        }

        private static string EscapeTab(string s)   => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        private static string UnescapeTab(string s) => s.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");

        // ════════════════════════════════════════════════════════════════════
        // STATISTICS HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static int CountStudents(DataTable t)
        {
            var nameCol = t.Columns.Cast<DataColumn>()
                           .FirstOrDefault(c => c.ColumnName.IndexOf("name",
                               StringComparison.OrdinalIgnoreCase) >= 0);
            if (nameCol == null) return t.Rows.Count;
            return t.Rows.Cast<DataRow>()
                    .Count(r => {
                        string nm = r[nameCol]?.ToString()?.Trim() ?? "";
                        return !string.IsNullOrEmpty(nm)
                            && !nm.Equals("Name", StringComparison.OrdinalIgnoreCase)
                            && !nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)
                            && nm.Length <= 60;
                    });
        }

        private static int CountPaid(DataTable t)
        {
            var totalCol = t.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c =>
                                c.ColumnName.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                c.ColumnName.IndexOf("fees",  StringComparison.OrdinalIgnoreCase) >= 0);
            if (totalCol == null) return 0;

            var nameCol = t.Columns.Cast<DataColumn>()
                           .FirstOrDefault(c => c.ColumnName.IndexOf("name",
                               StringComparison.OrdinalIgnoreCase) >= 0);

            return t.Rows.Cast<DataRow>()
                    .Count(r => {
                        if (nameCol != null) {
                            string nm = r[nameCol]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(nm) ||
                                nm.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                                nm.Length > 60) return false;
                        }
                        decimal.TryParse(r[totalCol]?.ToString()?.Trim() ?? "0",
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out decimal total);
                        return total <= 0m;
                    });
        }

        // ════════════════════════════════════════════════════════════════════
        // ORDERING HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static long AcademicOrder(string quarter, int calYear)
        {
            int q = quarter switch
            {
                "Aug-Oct" => 1,
                "Nov-Jan" => 2,
                "Feb-Apr" => 3,
                "May-Jul" => 4,
                _ => 0,
            };
            return (long)calYear * 10 + q;
        }

        // ════════════════════════════════════════════════════════════════════
        // MISC HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static int GetSemester(DataTable t)
        {
            if (t.ExtendedProperties.ContainsKey("Semester") &&
                int.TryParse(t.ExtendedProperties["Semester"]?.ToString(), out int s))
                return s;
            return 0;
        }

        private static int GetYear(DataTable t)
        {
            if (t.ExtendedProperties.ContainsKey("Year") &&
                int.TryParse(t.ExtendedProperties["Year"]?.ToString(), out int y))
                return y;
            return 1;
        }

        public static int CalendarYearForQuarter(string quarter, DateTime referenceDate)
            => referenceDate.Year;

        private static string SanitiseFilename(string key)
            => string.Concat(key.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        // ════════════════════════════════════════════════════════════════════
        // DATA CLASS
        // ════════════════════════════════════════════════════════════════════

        public class HistoryEntry
        {
            public string   Quarter           { get; set; }
            public int      CalendarYear      { get; set; }
            public int      Semester          { get; set; }
            public int      AcademicYear      { get; set; }
            public string   Period            { get; set; }
            public string   CourseInfo        { get; set; }
            public string   Department        { get; set; }
            public string   DataFile          { get; set; }
            public DateTime OriginalFileAdded { get; set; }
            public DateTime SnapshotTaken     { get; set; }
            public int      TotalStudents     { get; set; }
            public int      PaidStudents      { get; set; }

            // Derived
            public int    PendingStudents => TotalStudents - PaidStudents;
            public string QuarterLabel    => $"{Quarter} {CalendarYear}";
            public string SemesterLabel   => Semester > 0 ? $"Sem {Semester}" : $"Year {AcademicYear}";
        }
    }
}