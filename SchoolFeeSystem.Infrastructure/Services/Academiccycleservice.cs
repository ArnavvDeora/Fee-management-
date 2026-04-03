using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SchoolFeeSystem.Presentation.Services
{
    public class AcademicCycleService
    {
        // ── Quarter definitions ─────────────────────────────────────
        public static readonly IReadOnlyList<QuarterDef> Quarters =
            new List<QuarterDef>
            {
                new QuarterDef("Feb-Apr", 2,  4,  "FEB",  "APRIL"),
                new QuarterDef("May-Jul", 5,  7,  "MAY",  "JULY"),
                new QuarterDef("Aug-Oct", 8,  10, "AUG",  "OCT"),
                new QuarterDef("Nov-Jan", 11, 1,  "NOV",  "JAN"),
            };

        // Fine schedule
        public const int GraceDays = 15;
        public const decimal FineDay1Rate = 10m;     // days 16-45
        public const decimal FineDay2Rate = 20m;     // days 46-75
        public const decimal FineFlatMonth3 = 750m;  // day 76+

        // ── Persisted state ─────────────────────────────────────────
        public class CycleState
        {
            public Dictionary<string, string> LastQuarter { get; set; } = new();
            public Dictionary<string, string> QuarterStart { get; set; } = new();
            public Dictionary<string, int> CompletedQuarters { get; set; } = new();
            public string LastCheckedIso { get; set; } = DateTime.MinValue.ToString("O");
        }

        // ── Dependencies ─────────────────────────────────────────────
        private readonly CsvDataService _csv;
        private readonly PaymentLogService _log;
        private readonly string _stateFile;
        private CycleState _state;

        public AcademicCycleService(CsvDataService csv, PaymentLogService log)
        {
            _csv = csv;
            _log = log;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem");
            Directory.CreateDirectory(dir);
            _stateFile = Path.Combine(dir, "cycle_state.json");
            _state = LoadState();
        }

        // ═══════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════

        public static string CurrentQuarter(DateTime? on = null)
        {
            int m = (on ?? DateTime.Now).Month;
            foreach (var q in Quarters)
                if (q.Contains(m)) return q.Name;
            return Quarters[0].Name;
        }

        public static string NextQuarter(string q)
        {
            int i = Quarters.ToList().FindIndex(x => x.Name == q);
            return Quarters[(i + 1) % Quarters.Count].Name;
        }

        public static DateTime QuarterStartDate(string qName, DateTime? near = null)
        {
            var now = near ?? DateTime.Now;
            var q = Quarters.FirstOrDefault(x => x.Name == qName);
            if (q == null) return now;
            int yr = now.Year;
            if (qName == "Nov-Jan" && now.Month == 1) yr--;
            try { return new DateTime(yr, q.StartMonth, 1); }
            catch { return now; }
        }

        // Fine schedule:
        //   Days 1-15  : 0
        //   Days 16-45 : 10/day  (Month 1)
        //   Days 46-75 : 20/day  (Month 2)
        //   Day 76+    : flat 750 (Month 3)
        public static decimal CalculateFine(DateTime start, DateTime today)
        {
            int days = (today - start).Days;
            if (days <= GraceDays) return 0m;
            int over = days - GraceDays;
            if (over <= 30) return over * FineDay1Rate;
            if (over <= 60) return 30 * FineDay1Rate + (over - 30) * FineDay2Rate;
            return 30 * FineDay1Rate + 30 * FineDay2Rate + FineFlatMonth3;
        }

        public decimal LiveFineForSheet(string sheetName)
        {
            var start = PersistedStart(sheetName);
            return CalculateFine(start, DateTime.Now);
        }

        // ─── Main cycle check ───────────────────────────────────────
        // Call on app startup and on every file load.
        // FIX: sq==cur check now runs BEFORE the persisted-state check,
        //      so a current-quarter file is never accidentally advanced
        //      just because the stored LastQuarter is stale.
        public List<TransitionResult> RunCycleCheck()
        {
            var results = new List<TransitionResult>();
            string cur = CurrentQuarter();
            DateTime now = DateTime.Now;

            foreach (var sheet in _csv.GetAllSheets())
            {
                string name = sheet.TableName;
                string sq = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(sq)) continue;

                // FIX: Check the file's own quarter tag first.
                // If the file already belongs to the current quarter, just
                // record it and move on — never advance it.
                if (sq == cur)
                {
                    RecordCurrent(name, cur, now);
                    continue;
                }

                // Now check persisted state — if we already processed this
                // sheet in a previous run for the current quarter, skip.
                _state.LastQuarter.TryGetValue(name, out string last);
                if (last == cur) continue;

                // File is from a past quarter → advance it
                var r = Advance(sheet, cur);
                if (r != null)
                {
                    results.Add(r);
                    _state.CompletedQuarters.TryGetValue(name, out int done);
                    _state.CompletedQuarters[r.NewSheet] = done + 1;
                    RecordCurrent(r.NewSheet, cur, now);
                }
            }

            _state.LastCheckedIso = now.ToString("O");
            SaveState();
            CheckYearPromotion();
            return results;
        }

        // Inject live fines into a sheet before display in FeeCollection
        public void InjectLiveFines(DataTable sheet)
        {
            var start = PersistedStart(sheet.TableName);
            decimal fine = CalculateFine(start, DateTime.Now);
            if (fine <= 0) return;

            var fineCol = FC(sheet, "fine") ?? FC(sheet, "remarks");
            var nameCol = FC(sheet, "name");
            var pendCol = FC(sheet, "previous", "pending") ?? FC(sheet, "pending");
            if (fineCol == null) return;

            foreach (DataRow row in sheet.Rows)
            {
                if (!IsStudent(row, nameCol)) continue;

                // Only students with a pending balance owe a fine
                if (pendCol != null)
                {
                    if (!decimal.TryParse(row[pendCol]?.ToString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out decimal p) || p <= 0)
                        continue;
                }

                string ex = row[fineCol]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(ex) || ex == "0" || ex == "0.00")
                    row[fineCol] = fine.ToString("F2");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // CORE TRANSITION
        // ═══════════════════════════════════════════════════════════

        private TransitionResult Advance(DataTable old, string newQ)
        {
            try
            {
                var ns = old.Clone();
                string newPeriod = PeriodStr(newQ);

                // Rename columns that embed the old quarter period
                foreach (DataColumn c in ns.Columns)
                    if (HasQText(c.ColumnName))
                        c.ColumnName = ReplaceQText(c.ColumnName, newPeriod);

                // Table name keeps the base but gets a quarter suffix
                string baseName = old.TableName.Split(
                    new[] { "__" }, StringSplitOptions.None)[0];
                ns.TableName = $"{baseName}__{newQ.Replace("-", "")}";

                // Copy metadata
                foreach (string k in new[] { "Department", "Year", "InstituteName", "CourseInfo" })
                    if (old.ExtendedProperties.Contains(k))
                        ns.ExtendedProperties[k] = old.ExtendedProperties[k];
                ns.ExtendedProperties["Quarter"] = newQ;
                ns.ExtendedProperties["Period"] = newPeriod;

                // Old-sheet column references
                var oldTotal = FC(old, "total", "fees") ?? FC(old, "total");
                var oldPrevPend = FC(old, "previous", "pending");
                var oldQFee = FC(old, "quarterly");
                var oldName = FC(old, "name");
                var oldCat = FC(old, "category");

                // New-sheet column references
                var nsPrevPend = FC(ns, "previous", "pending");
                var nsFine = FC(ns, "fine") ?? FC(ns, "remarks");

                foreach (DataRow or in old.Rows)
                {
                    if (!IsStudent(or, oldName)) continue;

                    DataRow nr = ns.NewRow();

                    // ── Step 1: Copy identity columns; reset all fee cols to "0" ──
                    foreach (DataColumn oc in old.Columns)
                    {
                        string nc = MatchNewCol(ns, oc.ColumnName, newPeriod);
                        if (nc == null) continue;
                        nr[nc] = IsId(oc.ColumnName) ? or[oc] : (object)"0";
                    }

                    // ── Step 2: Carry forward unpaid balance → Previous Quarter Pending ──
                    // We use the old TOTAL column because that is what the student
                    // actually owed (quarterly + any previous carry already included).
                    decimal carry = 0m;
                    if (oldTotal != null)
                    {
                        decimal.TryParse(or[oldTotal]?.ToString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out carry);
                    }
                    else
                    {
                        // Fallback: sum previous pending + quarterly from old sheet
                        decimal pp = 0m, qf = 0m;
                        if (oldPrevPend != null)
                            decimal.TryParse(or[oldPrevPend]?.ToString(),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out pp);
                        if (oldQFee != null)
                            decimal.TryParse(or[oldQFee]?.ToString(),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out qf);
                        carry = pp + qf;
                    }

                    if (nsPrevPend != null && carry > 0)
                        nr[nsPrevPend.ColumnName] = carry.ToString("F2");

                    // ── Step 3: Restore quarterly fee for the new quarter ──────────────
                    // Rule: SC, ST, and any FW (fee-waiver) category → always ₹0.
                    //       OBC, GEN, BC (non-FW) → carry forward the same quarterly amount.
                    if (oldQFee != null)
                    {
                        var nsQFee = FC(ns, "quarterly");
                        if (nsQFee != null)
                        {
                            // Read category from old row
                            string cat = oldCat != null
                                ? or[oldCat]?.ToString()?.Trim().ToUpper() ?? ""
                                : "";

                            bool isFreeCategory =
                                cat == "SC" ||
                                cat == "ST" ||
                                cat.Contains("FW");  // covers BC(FW), OBC(FW), GEN(FW), FW BC, etc.

                            if (isFreeCategory)
                            {
                                nr[nsQFee.ColumnName] = "0";
                            }
                            else
                            {
                                // Carry the same quarterly fee the student had last quarter
                                decimal.TryParse(or[oldQFee]?.ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out decimal oldQ);
                                nr[nsQFee.ColumnName] = oldQ.ToString("F2");
                            }
                        }
                    }

                    // ── Step 4: Recalculate TOTAL = new quarterly + new previous pending ──
                    var nsTotalCol = FC(ns, "total", "fees") ?? FC(ns, "total");
                    if (nsTotalCol != null)
                    {
                        decimal newQAmt = 0m;
                        decimal newPrev = 0m;
                        var nsQFeeCol = FC(ns, "quarterly");
                        if (nsQFeeCol != null)
                            decimal.TryParse(nr[nsQFeeCol.ColumnName]?.ToString(),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out newQAmt);
                        if (nsPrevPend != null)
                            decimal.TryParse(nr[nsPrevPend.ColumnName]?.ToString(),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out newPrev);
                        nr[nsTotalCol.ColumnName] = (newQAmt + newPrev).ToString("F2");
                    }

                    // ── Step 5: Reset fine for new quarter ────────────────────────────
                    if (nsFine != null) nr[nsFine.ColumnName] = "0";

                    ns.Rows.Add(nr);
                }

                string dept = old.ExtendedProperties["Department"]?.ToString() ?? "";
                _csv.AddSheetToLoadedFiles(ns, dept);

                _log.LogPayment(
                    studentName: "[System]",
                    studentId: "",
                    sheetName: ns.TableName,
                    courseName: old.ExtendedProperties["CourseInfo"]?.ToString() ?? "",
                    period: newPeriod,
                    amountPaid: 0,
                    paymentMode: "Auto Transition",
                    previousBalance: 0,
                    newBalance: 0,
                    phoneNumber: "",
                    guardianName: "",
                    remarks: $"Auto-advanced from {old.ExtendedProperties["Quarter"]} → {newQ}. " +
                             $"{ns.Rows.Count} students.");

                return new TransitionResult
                {
                    OldSheet = old.TableName,
                    NewSheet = ns.TableName,
                    NewQuarter = newQ,
                    StudentsCarried = ns.Rows.Count
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cycle] Advance failed: {ex.Message}");
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // YEAR PROMOTION CHECK
        // ═══════════════════════════════════════════════════════════

        private void CheckYearPromotion()
        {
            var groups = _csv.GetAllSheets()
                .GroupBy(s =>
                    $"{s.ExtendedProperties["Department"]?.ToString()}|" +
                    $"{s.ExtendedProperties["Year"]?.ToString()}");

            foreach (var g in groups)
            {
                var parts = g.Key.Split('|');
                if (parts.Length < 2) continue;
                string dept = parts[0];
                if (!int.TryParse(parts[1], out int yr) || yr <= 0) continue;

                int qSeen = g.Select(t => t.ExtendedProperties["Quarter"]?.ToString())
                             .Where(q => !string.IsNullOrEmpty(q)).Distinct().Count();
                if (qSeen < 4) continue;

                bool higherExists = _csv.GetAllSheets().Any(t =>
                {
                    int.TryParse(t.ExtendedProperties["Year"]?.ToString(), out int ty);
                    return t.ExtendedProperties["Department"]?.ToString() == dept && ty == yr + 1;
                });
                if (higherExists) continue;

                // 3-year diploma default; override per dept as needed
                int maxYears = 3;
                bool isLast = yr >= maxYears;
                try { _csv.PromoteStudentsToNextYear(dept, yr, isLast); }
                catch (Exception ex)
                { System.Diagnostics.Debug.WriteLine($"[Cycle] AutoPromote: {ex.Message}"); }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // COLUMN HELPERS
        // ═══════════════════════════════════════════════════════════

        private static DataColumn FC(DataTable t, params string[] kw)
            => t.Columns.Cast<DataColumn>()
               .FirstOrDefault(c => kw.All(k =>
                   c.ColumnName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

        private static bool IsStudent(DataRow row, DataColumn nc)
        {
            if (nc == null) return true;
            string nm = row[nc]?.ToString()?.Trim() ?? "";
            return !string.IsNullOrEmpty(nm) && nm.Length <= 60
                && !nm.Equals("Name", StringComparison.OrdinalIgnoreCase)
                && !nm.StartsWith("Note", StringComparison.OrdinalIgnoreCase)
                && !nm.Contains(":-");
        }

        // IsId controls which columns are preserved as-is during quarter advance
        // (everything else gets reset to "0").
        // Identity columns: student personal data + fixed per-student charges.
        private static bool IsId(string col)
        {
            string n = col.ToLower();
            return n.Contains("name") ||
                   n.Contains("father") ||
                   n.Contains("mother") ||
                   n.Contains("category") ||
                   n.Contains("section") ||
                   n.StartsWith("_") ||
                   n.Contains("scholarship") ||
                   n.Contains("hostel") ||
                   n.Contains("roll") ||
                   n.Contains("sr no") ||
                   n.Contains("sr.") ||
                   // Fixed per-student charges carried every quarter:
                   n.Contains("stationary") ||
                   n.Contains("welfare") ||
                   n.Contains("insurance") ||
                   n.Contains("red cross") ||
                   n.Contains("student activ") ||
                   n.Contains("institutional") ||
                   n.Contains("refundable");
        }

        private static bool HasQText(string col)
        {
            string u = col.ToUpper();
            return Quarters.Any(q =>
                u.Contains(q.StartMonthAbbr.ToUpper()) ||
                u.Contains(q.EndMonthAbbr.ToUpper()));
        }

        private static readonly System.Text.RegularExpressions.Regex QRx =
            new System.Text.RegularExpressions.Regex(
                @"\(?\s*(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}\s+[Tt][Oo]\s+" +
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{4}\s*\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static string ReplaceQText(string col, string newP)
            => QRx.Replace(col, $"({newP})");

        private static string MatchNewCol(DataTable ns, string oldName, string newPeriod)
        {
            if (ns.Columns.Contains(oldName)) return oldName;
            string ob = QRx.Replace(oldName, "").Trim().ToLower();
            foreach (DataColumn nc in ns.Columns)
            {
                string nb = QRx.Replace(nc.ColumnName, "").Trim().ToLower();
                if (ob == nb) return nc.ColumnName;
            }
            return null;
        }

        private static string PeriodStr(string q)
        {
            int y = DateTime.Now.Year;
            return q switch
            {
                "Feb-Apr" => $"FEB {y} to APRIL {y}",
                "May-Jul" => $"MAY {y} to JULY {y}",
                "Aug-Oct" => $"AUG {y} to OCT {y}",
                "Nov-Jan" => $"NOV {y} to JAN {y + 1}",
                _ => $"{q} {y}",
            };
        }

        // ═══════════════════════════════════════════════════════════
        // STATE PERSISTENCE
        // ═══════════════════════════════════════════════════════════

        private DateTime PersistedStart(string sheetName)
        {
            if (_state.QuarterStart.TryGetValue(sheetName, out string iso)
                && DateTime.TryParse(iso, out DateTime d)) return d;
            return QuarterStartDate(CurrentQuarter());
        }

        private void RecordCurrent(string name, string q, DateTime now)
        {
            _state.LastQuarter[name] = q;
            if (!_state.QuarterStart.ContainsKey(name))
                _state.QuarterStart[name] = QuarterStartDate(q, now).ToString("O");
        }

        private CycleState LoadState()
        {
            try
            {
                if (File.Exists(_stateFile))
                    return JsonSerializer.Deserialize<CycleState>(
                               File.ReadAllText(_stateFile)) ?? new CycleState();
            }
            catch { }
            return new CycleState();
        }

        private void SaveState()
        {
            try
            {
                File.WriteAllText(_stateFile,
                    JsonSerializer.Serialize(_state,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════
        // VALUE TYPES
        // ═══════════════════════════════════════════════════════════

        public class TransitionResult
        {
            public string OldSheet { get; set; }
            public string NewSheet { get; set; }
            public string NewQuarter { get; set; }
            public int StudentsCarried { get; set; }
        }

        public class QuarterDef
        {
            public string Name { get; }
            public int StartMonth { get; }
            public int EndMonth { get; }
            public string StartMonthAbbr { get; }
            public string EndMonthAbbr { get; }

            public QuarterDef(string n, int s, int e, string sa, string ea)
            { Name = n; StartMonth = s; EndMonth = e; StartMonthAbbr = sa; EndMonthAbbr = ea; }

            public bool Contains(int month)
            {
                if (StartMonth <= EndMonth) return month >= StartMonth && month <= EndMonth;
                return month >= StartMonth || month <= EndMonth;
            }
        }
    }
}