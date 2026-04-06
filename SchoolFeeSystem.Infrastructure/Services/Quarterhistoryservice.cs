using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SchoolFeeSystem.Presentation.Services
{
    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterHistoryService
    //
    //  Responsibility: maintain a permanent, per-sheet archive so the user can
    //  browse back through every quarter's snapshot and see who paid / who did
    //  not in any past period.
    //
    //  Storage:  %AppData%\SchoolFeeSystem\history\
    //              <sheetKey>.json          → list of HistoryEntry (index)
    //              <sheetKey>_<quarter>.bin → serialised DataTable rows (CSV)
    //
    //  Key concepts
    //  ────────────
    //  • A "sheet key" is  "<Dept>|<Year>|<Sem>"  e.g. "ME|2|3"
    //    This is stable across quarter transitions so all quarters of the same
    //    class are grouped together.
    //
    //  • Every time AcademicCycleService advances a sheet to a new quarter it
    //    calls  Snapshot(oldSheet, oldQuarter)  BEFORE replacing the data.
    //    The snapshot captures the *completed* quarter so payment status is final.
    //
    //  • The UI calls  GetHistory(sheetKey)  → list of HistoryEntry (sorted by
    //    academic quarter order).  Then  LoadSnapshot(entry)  → DataTable.
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
        /// <param name="sheet">The DataTable being archived (before reset).</param>
        /// <param name="quarter">Quarter name, e.g. "Feb-Apr".</param>
        /// <param name="calendarYear">The calendar year this quarter belongs to.</param>
        /// <param name="originalFileAddedDate">
        ///     When the source Excel file was first imported — shown on the card.
        /// </param>
        public void Snapshot(DataTable sheet, string quarter, int calendarYear,
                             DateTime originalFileAddedDate)
        {
            try
            {
                string key = SheetKey(sheet);
                var index = LoadIndex(key);

                // Avoid double-snapshotting the same quarter
                if (index.Any(e => e.Quarter == quarter && e.CalendarYear == calendarYear))
                    return;

                // ── Serialise all student rows as CSV text ──────────────────────
                string dataFile = DataFilePath(key, quarter, calendarYear);
                WriteCsv(sheet, dataFile);

                // ── Record in the index ─────────────────────────────────────────
                int sem = GetSemester(sheet);
                int yr = GetYear(sheet);

                index.Add(new HistoryEntry
                {
                    Quarter = quarter,
                    CalendarYear = calendarYear,
                    Semester = sem,
                    AcademicYear = yr,
                    Period = sheet.ExtendedProperties["Period"]?.ToString() ?? "",
                    CourseInfo = sheet.ExtendedProperties["CourseInfo"]?.ToString() ?? "",
                    Department = sheet.ExtendedProperties["Department"]?.ToString() ?? "",
                    DataFile = dataFile,
                    OriginalFileAdded = originalFileAddedDate,
                    SnapshotTaken = DateTime.Now,
                    TotalStudents = CountStudents(sheet),
                    PaidStudents = CountPaid(sheet),
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
        /// Also call this when a file is first imported so quarter Q0 (the file's own
        /// quarter) appears in the history timeline as the "Original File" entry.
        /// </summary>
        public void RecordImport(DataTable sheet, DateTime importedOn)
        {
            string q = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
            int yr = CalendarYearForQuarter(q, importedOn);
            Snapshot(sheet, q, yr, importedOn);
        }

        /// <summary>
        /// Returns the history entries for a sheet key, sorted in academic order
        /// (Aug-Oct → Nov-Jan → Feb-Apr → May-Jul, year wrapping correctly).
        /// </summary>
        public List<HistoryEntry> GetHistory(string sheetKey)
        {
            var index = LoadIndex(sheetKey);
            return index.OrderBy(e => AcademicOrder(e.Quarter, e.CalendarYear)).ToList();
        }

        /// <summary>
        /// Returns the history for a DataTable by computing its key.
        /// </summary>
        public List<HistoryEntry> GetHistory(DataTable sheet)
            => GetHistory(SheetKey(sheet));

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
        // SHEET KEY
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Stable identifier:  DEPT|YEAR|SEM  —  groups all quarters of one class.
        /// e.g.  "ME|2|3"
        /// </summary>
        public static string SheetKey(DataTable sheet)
        {
            string dept = sheet.ExtendedProperties["Department"]?.ToString() ?? "UNK";
            string yr = sheet.ExtendedProperties["Year"]?.ToString() ?? "0";
            string sem = sheet.ExtendedProperties["Semester"]?.ToString() ?? "0";
            return $"{dept}|{yr}|{sem}";
        }

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
        // CSV SERIALISATION (no binary, plain text — survives schema changes)
        // ════════════════════════════════════════════════════════════════════

        private static void WriteCsv(DataTable table, string path)
        {
            using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);

            // Header
            var cols = table.Columns.Cast<DataColumn>().ToList();
            sw.WriteLine(string.Join("\t", cols.Select(c => EscapeTab(c.ColumnName))));

            // Rows
            foreach (DataRow row in table.Rows)
            {
                sw.WriteLine(string.Join("\t",
                    cols.Select(c => EscapeTab(row[c]?.ToString() ?? ""))));
            }
        }

        private static DataTable ReadCsv(string path, string quarter, string period,
                                          string dept, int year, int sem)
        {
            var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            if (lines.Length == 0) return new DataTable();

            var table = new DataTable();
            var headers = lines[0].Split('\t').Select(UnescapeTab).ToArray();
            foreach (var h in headers)
                table.Columns.Add(h);

            for (int i = 1; i < lines.Length; i++)
            {
                var cells = lines[i].Split('\t').Select(UnescapeTab).ToArray();
                var row = table.NewRow();
                for (int c = 0; c < headers.Length && c < cells.Length; c++)
                    row[c] = cells[c];
                table.Rows.Add(row);
            }

            // Restore metadata so the DataView renders correctly
            table.ExtendedProperties["Quarter"] = quarter;
            table.ExtendedProperties["Period"] = period;
            table.ExtendedProperties["Department"] = dept;
            table.ExtendedProperties["Year"] = year.ToString();
            table.ExtendedProperties["Semester"] = sem.ToString();

            return table;
        }

        private static string EscapeTab(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
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
                    .Count(r =>
                    {
                        string nm = r[nameCol]?.ToString()?.Trim() ?? "";
                        return !string.IsNullOrEmpty(nm)
                            && !nm.Equals("Name", StringComparison.OrdinalIgnoreCase)
                            && !nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)
                            && nm.Length <= 60;
                    });
        }

        private static int CountPaid(DataTable t)
        {
            // A student is "paid" when their Total Fees column is 0 (or absent)
            var totalCol = t.Columns.Cast<DataColumn>()
                            .FirstOrDefault(c =>
                                c.ColumnName.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                c.ColumnName.IndexOf("fees", StringComparison.OrdinalIgnoreCase) >= 0);

            if (totalCol == null) return 0;

            var nameCol = t.Columns.Cast<DataColumn>()
                           .FirstOrDefault(c => c.ColumnName.IndexOf("name",
                               StringComparison.OrdinalIgnoreCase) >= 0);

            return t.Rows.Cast<DataRow>()
                    .Count(r =>
                    {
                        if (nameCol != null)
                        {
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

        // Returns a sortable integer so quarters list in academic order:
        //   Aug-Oct (Q1) < Nov-Jan (Q2) < Feb-Apr (Q3) < May-Jul (Q4)
        // Academic year starts in August, so Nov-Jan of the SAME calendar year
        // is LATER than Aug-Oct but EARLIER than Feb-Apr of the NEXT calendar year.
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

            // Academic year for Aug-Oct / Nov-Jan belongs to calYear;
            // Feb-Apr / May-Jul belong to calYear (same calendar year, later in sequence).
            // So sort key = calYear * 10 + q gives correct ordering.
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

        /// <summary>
        /// Determines which calendar year a quarter belongs to.
        /// Aug-Oct and Nov-Jan → same calendar year as <paramref name="referenceDate"/>.
        /// Feb-Apr and May-Jul → same calendar year as <paramref name="referenceDate"/>.
        /// (Nov-Jan wraps: the "Jan" is next calendar year but we file it under
        ///  the year it STARTED, which is the referenceDate year.)
        /// </summary>
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
            /// <summary>Quarter label: "Aug-Oct", "Nov-Jan", "Feb-Apr", "May-Jul"</summary>
            public string Quarter { get; set; }

            /// <summary>Calendar year this quarter started in.</summary>
            public int CalendarYear { get; set; }

            /// <summary>Semester number (1-6 for 3-yr diploma).</summary>
            public int Semester { get; set; }

            /// <summary>Academic year (1-3 for 3-yr diploma).</summary>
            public int AcademicYear { get; set; }

            /// <summary>Period string from the Excel header, e.g. "FEB 2026 TO APRIL 2026"</summary>
            public string Period { get; set; }

            /// <summary>Course description row from Excel, e.g. "Diploma - ME - 2nd Year"</summary>
            public string CourseInfo { get; set; }

            /// <summary>Department code, e.g. "ME".</summary>
            public string Department { get; set; }

            /// <summary>Absolute path to the CSV snapshot file.</summary>
            public string DataFile { get; set; }

            /// <summary>When the original Excel file was first imported.</summary>
            public DateTime OriginalFileAdded { get; set; }

            /// <summary>When this snapshot was taken (= when the quarter was advanced).</summary>
            public DateTime SnapshotTaken { get; set; }

            /// <summary>Count of real student rows at snapshot time.</summary>
            public int TotalStudents { get; set; }

            /// <summary>Students with zero total dues at snapshot time (i.e. fully paid).</summary>
            public int PaidStudents { get; set; }

            // Derived for UI convenience
            public int PendingStudents => TotalStudents - PaidStudents;
            public string QuarterLabel => $"{Quarter} {CalendarYear}";
            public string SemesterLabel => Semester > 0 ? $"Sem {Semester}" : $"Year {AcademicYear}";
        }
    }
}