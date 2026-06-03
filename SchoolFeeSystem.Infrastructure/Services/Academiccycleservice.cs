using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SchoolFeeSystem.Presentation.Services
{
    // ══════════════════════════════════════════════════════════════════════════
    //  AcademicCycleService  (Rewritten)
    //
    //  Academic year model
    //  ───────────────────
    //  Session starts in AUGUST.  Four quarters per year:
    //
    //   Q1  Aug-Oct   → end of Q1: nothing changes
    //   Q2  Nov-Jan   → end of Q2: Semester increases  (Sem 1→2, 3→4, 5→6)
    //   Q3  Feb-Apr   → end of Q3: nothing changes
    //   Q4  May-Jul   → end of Q4: Semester increases AND Year increases
    //                              (Sem 2→3, 4→5, 6→7…)
    //                              If Year was the final year → student PASSES OUT
    //
    //  Per-quarter fee carry-forward
    //  ──────────────────────────────
    //  When the real-world date crosses into a new quarter, every loaded sheet
    //  whose Quarter tag is BEHIND the current quarter is automatically advanced:
    //    • Identity columns (Name, Father, Category…) → copied as-is
    //    • Fee columns → reset to "0"
    //    • Previous Quarter Pending column → receives the old TOTAL FEES value
    //      (i.e. whatever was still owed carries forward)
    //    • Quarterly Fee column → same amount as last quarter (except free categories)
    //
    //  History snapshot
    //  ─────────────────
    //  Before each advance, the completed quarter's data is handed to
    //  QuarterHistoryService.Snapshot() so the admin can browse past quarters.
    //
    //  Semester / Year promotion
    //  ──────────────────────────
    //  Happens automatically inside RunCycleCheck():
    //    After Q2 ends  → Semester += 1   (Nov-Jan → Feb-Apr boundary)
    //    After Q4 ends  → Semester += 1, Year += 1  (May-Jul → Aug-Oct boundary)
    //  The ExtendedProperties on each DataTable are updated in-memory and
    //  persisted via CsvDataService.SaveFile().
    // ══════════════════════════════════════════════════════════════════════════

    public class AcademicCycleService
    {
        // ── Quarter definitions ─────────────────────────────────────────────
        //  Order matters: this is the academic order within one session.
        public static readonly IReadOnlyList<QuarterDef> Quarters =
            new List<QuarterDef>
            {
                new QuarterDef("Aug-Oct", 8,  10, "AUG",  "OCT"),   // Q1 – session start
                new QuarterDef("Nov-Jan", 11,  1, "NOV",  "JAN"),   // Q2 – semester bump
                new QuarterDef("Feb-Apr",  2,  4, "FEB",  "APRIL"), // Q3
                new QuarterDef("May-Jul",  5,  7, "MAY",  "JULY"),  // Q4 – year + semester bump
            };

        // ── Fine schedule ───────────────────────────────────────────────────
        public const int GraceDays = 15;
        public const decimal FineDay1Rate = 10m;   // days 16-45  (~month 1 late)
        public const decimal FineDay2Rate = 20m;   // days 46-75  (~month 2 late)
        public const decimal FineFlatMonth3 = 1000m;  // day 76+

        // ── Persisted state ─────────────────────────────────────────────────
        public class CycleState
        {
            /// <summary>Sheet name → last quarter label it was processed for.</summary>
            public Dictionary<string, string> LastQuarter { get; set; } = new();
            /// <summary>Sheet name → ISO date when this quarter started (for fine calc).</summary>
            public Dictionary<string, string> QuarterStart { get; set; } = new();
            /// <summary>Sheet name → number of quarter transitions completed.</summary>
            public Dictionary<string, int> CompletedQuarters { get; set; } = new();
            /// <summary>Sheet name → ISO date the original file was first imported.</summary>
            public Dictionary<string, string> OriginalImportDate { get; set; } = new();
            public string LastCheckedIso { get; set; } = DateTime.MinValue.ToString("O");
        }

        // ── Dependencies ────────────────────────────────────────────────────
        private readonly CsvDataService _csv;
        private readonly PaymentLogService _log;
        private readonly QuarterHistoryService _history;
        private readonly string _stateFile;
        private CycleState _state;

        public AcademicCycleService(CsvDataService csv, PaymentLogService log,
                                    QuarterHistoryService history)
        {
            _csv = csv;
            _log = log;
            _history = history;

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchoolFeeSystem");
            Directory.CreateDirectory(dir);
            _stateFile = Path.Combine(dir, "cycle_state.json");
            _state = LoadState();
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC STATIC HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the quarter name that the given date falls in.
        /// Academic year starts Aug, so Aug-Oct = Q1.
        /// </summary>
        public static string CurrentQuarter(DateTime? on = null)
        {
            int m = (on ?? DateTime.Now).Month;
            foreach (var q in Quarters)
                if (q.Contains(m)) return q.Name;
            return Quarters[0].Name;
        }

        /// <summary>Next quarter in the academic cycle (wraps May-Jul → Aug-Oct).</summary>
        public static string NextQuarter(string q)
        {
            int i = Quarters.ToList().FindIndex(x => x.Name == q);
            return Quarters[(i + 1) % Quarters.Count].Name;
        }

        /// <summary>
        /// The calendar date on which a quarter starts.
        /// Nov-Jan is special: if we are currently IN January, the quarter
        /// started in November of the PREVIOUS calendar year.
        /// </summary>
        public static DateTime QuarterStartDate(string qName, DateTime? near = null)
        {
            var now = near ?? DateTime.Now;
            var q = Quarters.FirstOrDefault(x => x.Name == qName);
            if (q == null) return now;

            int yr = now.Year;

            // Nov-Jan: if we are in January the quarter started last November
            if (qName == "Nov-Jan" && now.Month == 1)
                yr--;

            try { return new DateTime(yr, q.StartMonth, 1); }
            catch { return now; }
        }

        /// <summary>
        /// Fine schedule:
        ///   Days  1-15  → ₹0
        ///   Days 16-EOM → ₹10/day
        ///   Month 2     → ₹20/day
        ///   Month 3+    → ₹1,000 flat per started month
        /// </summary>
        public static decimal CalculateFine(DateTime start, DateTime today)
        {
            int days = (today - start).Days;
            if (days <= GraceDays) return 0m;
            int over = days - GraceDays;
            if (over <= 30) return over * FineDay1Rate;
            if (over <= 60) return 30 * FineDay1Rate + (over - 30) * FineDay2Rate;
            // Month 3+: count each started month beyond day 60
            int extraDays = over - 60;
            int extraMonths = (extraDays / 30) + 1; // each started month counts
            return 30 * FineDay1Rate + 30 * FineDay2Rate + extraMonths * FineFlatMonth3;
        }

        public decimal LiveFineForSheet(string sheetName)
            => CalculateFine(PersistedStart(sheetName), DateTime.Now);

        /// <summary>
        /// Returns when the original Excel file for this sheet was imported.
        /// Shown in the UI as "File added on …".
        /// </summary>
        public DateTime GetOriginalImportDate(string sheetName)
        {
            if (_state.OriginalImportDate.TryGetValue(sheetName, out string iso)
                && DateTime.TryParse(iso, out DateTime d))
                return d;
            return DateTime.MinValue;
        }

        // ════════════════════════════════════════════════════════════════════
        // IMPORT RECORDING  (call from CsvDataService.LoadFile)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Call once each time a new Excel file is imported.
        /// Records the import timestamp so the UI can show "Original file added on …".
        /// Also takes an initial history snapshot so Q0 appears in the timeline.
        /// </summary>
        public void RecordFileImport(DataTable sheet)
        {
            string name = sheet.TableName;

            // Only record the import timestamp once (first import wins)
            if (!_state.OriginalImportDate.ContainsKey(name))
            {
                _state.OriginalImportDate[name] = DateTime.Now.ToString("O");
                SaveState();
            }

            // Only default Quarter to CurrentQuarter when it's truly empty or unrecognised.
            // If it already holds a valid quarter name (e.g. "Nov-Jan" from the sidecar),
            // leave it alone so RunCycleCheck can do the correct one-time transition
            // rather than silently overwriting the saved value.
            string existingQ = sheet.ExtendedProperties["Quarter"]?.ToString() ?? "";
            bool isKnownQuarter = Quarters.Any(q =>
                string.Equals(q.Name, existingQ, StringComparison.OrdinalIgnoreCase));
            if (!isKnownQuarter)
                sheet.ExtendedProperties["Quarter"] = CurrentQuarter();

            DateTime imported = GetOriginalImportDate(name);
            if (imported == DateTime.MinValue) imported = DateTime.Now;

            // FIX: use SnapshotCurrentQuarter so the initial Feb-Apr (or whatever
            // quarter the file belongs to) immediately appears in the history timeline.
            _history.SnapshotCurrentQuarter(sheet, imported);
        }

        // ════════════════════════════════════════════════════════════════════
        // MAIN CYCLE CHECK
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Call on every app startup (and after any file import).
        /// For each loaded sheet, checks whether real time has moved into a new
        /// quarter. If so, carries forward balances and updates semester/year.
        /// </summary>
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

                // ── Sheet is ALREADY on the current quarter ────────────────────────
                if (sq == cur)
                {
                    RecordCurrent(name, cur, now);

                    // FIX (a): snapshot the current live quarter on every startup so
                    // it appears in the timeline even before any transition occurs.
                    // SnapshotCurrentQuarter() is idempotent — safe to call repeatedly.
                    DateTime importedOn = GetOriginalImportDate(name);
                    if (importedOn == DateTime.MinValue) importedOn = now;
                    _history.SnapshotCurrentQuarter(sheet, importedOn);
                    continue;
                }

                // ── Already transitioned in a previous run for this quarter ────────
                _state.LastQuarter.TryGetValue(name, out string last);
                if (last == cur) continue;

                // ── Sheet is from a past quarter → advance it ──────────────────────
                DateTime imported = GetOriginalImportDate(name);
                if (imported == DateTime.MinValue) imported = now;

                // FIX (b): use the STARTING date of the OLD quarter to determine its
                // calendar year, not DateTime.Now, so Feb-Apr snapshotted in May is
                // still filed as CalendarYear = the year Feb started.
                DateTime oldQStart = QuarterStartDate(sq, now);
                int calYear = QuarterHistoryService.CalendarYearForQuarter(sq, oldQStart);

                // Snapshot the completed quarter BEFORE data is overwritten
                _history.Snapshot(sheet, sq, calYear, imported);

                // Compute new semester / year BEFORE advancing the sheet
                var (newSem, newYear, isPassout) = ComputePromotion(sheet, sq);

                // Build the new quarter DataTable
                var r = Advance(sheet, cur, newSem, newYear);
                if (r != null)
                {
                    results.Add(r);
                    _state.CompletedQuarters.TryGetValue(name, out int done);
                    _state.CompletedQuarters[r.NewSheet] = done + 1;
                    RecordCurrent(r.NewSheet, cur, now);

                    // KEY FIX: mark the OLD sheet as "already at cur" AND remove it from
                    // memory so SaveFile never writes it back to disk. Without this the old
                    // DataTable reloads on next startup, RunCycleCheck sees its past quarter,
                    // and fires Advance() again — wiping data in an infinite loop.
                    _state.LastQuarter[r.OldSheet] = cur;
                    _csv.RemoveSheet(r.OldSheet);

                    // Propagate the original import date to the new sheet name
                    if (!_state.OriginalImportDate.ContainsKey(r.NewSheet))
                        _state.OriginalImportDate[r.NewSheet] = imported.ToString("O");

                    // FIX (c): immediately snapshot the NEW (live) quarter so it shows
                    // in the history panel right after the transition — the user does not
                    // have to wait until the NEXT transition for it to appear.
                    var newSheet = _csv.GetAllSheets()
                                       .FirstOrDefault(s => s.TableName == r.NewSheet);
                    if (newSheet != null)
                        _history.SnapshotCurrentQuarter(newSheet, imported);

                    if (isPassout)
                        _log.LogPayment(
                            studentName: "[System]",
                            studentId: "", sheetName: r.NewSheet,
                            courseName: sheet.ExtendedProperties["CourseInfo"]?.ToString() ?? "",
                            period: PeriodStr(cur),
                            amountPaid: 0, paymentMode: "Auto Passout",
                            previousBalance: 0, newBalance: 0,
                            phoneNumber: "", guardianName: "",
                            remarks: $"Students moved to PASSOUT from {name}.");
                }
            }

            _state.LastCheckedIso = now.ToString("O");
            SaveState();
            return results;
        }

        // ════════════════════════════════════════════════════════════════════
        // SEMESTER / YEAR PROMOTION RULES
        // ════════════════════════════════════════════════════════════════════

        // Quarter that just ENDED → what happens to Semester and Year?
        //
        //   Ended Q2 (Nov-Jan)  → Semester +1 only
        //                          (Sem 1→2, 3→4, 5→6)
        //   Ended Q4 (May-Jul)  → Semester +1  AND  Year +1
        //                          (Sem 2→3, 4→5)
        //                          If Year == maxYears → PASSOUT
        //   Ended Q1 / Q3       → no change
        //
        private (int newSem, int newYear, bool passout) ComputePromotion(
            DataTable sheet, string completedQuarter)
        {
            int sem = GetSemester(sheet);
            int year = GetYear(sheet);
            string dept = sheet.ExtendedProperties["Department"]?.ToString() ?? "";
            int maxYears = MaxYears(dept);

            // Q2 end: semester only
            if (completedQuarter == "Nov-Jan")
            {
                return (sem + 1, year, false);
            }

            // Q4 end: semester + year
            if (completedQuarter == "May-Jul")
            {
                int newYear = year + 1;
                int newSem = sem + 1;
                bool passout = newYear > maxYears;
                if (passout) newYear = 0; // 0 signals PASSOUT
                return (newSem, newYear, passout);
            }

            // Q1 / Q3 end: no change
            return (sem, year, false);
        }

        // ════════════════════════════════════════════════════════════════════
        // CORE TRANSITION  (quarter advance)
        // ════════════════════════════════════════════════════════════════════

        private TransitionResult Advance(DataTable old, string newQ,
                                          int newSem, int newYear)
        {
            try
            {
                var ns = old.Clone();
                string newPeriod = PeriodStr(newQ);

                // Rename columns that embed the old quarter period text
                foreach (DataColumn c in ns.Columns)
                    if (HasQText(c.ColumnName))
                        c.ColumnName = ReplaceQText(c.ColumnName, newPeriod);

                // Table name: base + quarter suffix
                string baseName = old.TableName.Split(
                    new[] { "__" }, StringSplitOptions.None)[0];
                ns.TableName = $"{baseName}__{newQ.Replace("-", "")}";

                // Copy metadata — update quarter, semester, year
                // DisplayName is the admin-assigned label (e.g. "Mech Engineering — Sem 3")
                // and MUST be carried forward so the course card keeps its custom name.
                // OriginalSheetName is used by CsvDataService.AddSheetToLoadedFiles to
                // attach the new DataTable to the correct source file so SaveFile() knows
                // which path to write it to.
                foreach (string k in new[] { "Department", "InstituteName", "CourseInfo", "DisplayName", "OriginalSheetName" })
                    if (old.ExtendedProperties.Contains(k))
                        ns.ExtendedProperties[k] = old.ExtendedProperties[k];

                // If the old sheet didn't have OriginalSheetName set, seed it now from
                // the old sheet's own TableName so the lineage is traceable.
                if (!ns.ExtendedProperties.Contains("OriginalSheetName") ||
                    string.IsNullOrEmpty(ns.ExtendedProperties["OriginalSheetName"]?.ToString()))
                    ns.ExtendedProperties["OriginalSheetName"] = old.TableName;

                ns.ExtendedProperties["Quarter"] = newQ;
                ns.ExtendedProperties["Period"] = newPeriod;
                ns.ExtendedProperties["Semester"] = newSem.ToString();

                // Year: 0 means PASSOUT, otherwise use newYear
                if (newYear <= 0)
                    ns.ExtendedProperties["Department"] = "PASSOUT";
                else
                    ns.ExtendedProperties["Year"] = newYear.ToString();

                // ── Column references on the OLD sheet ────────────────────────
                var oldTotal = FC(old, "total", "fees") ?? FC(old, "total");
                var oldPrevPend = FC(old, "previous", "pending") ?? FC(old, "previous");
                var oldQFee = FC(old, "quarterly");
                var oldName = FC(old, "name");
                var oldCat = FC(old, "category");

                // ── Column references on the NEW sheet ────────────────────────
                var nsPrevPend = FC(ns, "previous", "pending") ?? FC(ns, "previous");
                var nsFine = FC(ns, "fine") ?? FC(ns, "remarks");

                // ── Ensure Fine_Start_Date column exists on the new sheet ─────────
                // This tracks the earliest date from which a student's fine should
                // accrue. If they carried over an unpaid balance from the previous
                // quarter, their fine reference date stays at the OLD quarter's start
                // (not the new quarter's start). If they are fully paid up, the new
                // quarter's start is used.
                const string FineStartCol = "Fine_Start_Date";
                if (!ns.Columns.Contains(FineStartCol))
                    ns.Columns.Add(FineStartCol, typeof(string));

                // Resolve the OLD quarter's start date for fine continuity.
                // This is stored in the OLD sheet's ExtendedProperties["QuarterStart"]
                // (cached by FineCalculationService) or we derive it from Period.
                DateTime oldQuarterStart;
                if (old.ExtendedProperties.ContainsKey("QuarterStart") &&
                    old.ExtendedProperties["QuarterStart"] is DateTime oqs)
                    oldQuarterStart = oqs;
                else
                {
                    var oldPeriod = old.ExtendedProperties["Period"]?.ToString() ?? "";
                    oldQuarterStart = FineCalculationService.TryParseQuarterStart(oldPeriod)
                                      ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                }

                foreach (DataRow or in old.Rows)
                {
                    if (!IsStudent(or, oldName)) continue;

                    DataRow nr = ns.NewRow();

                    // Step 1: copy identity columns; zero all fee columns
                    foreach (DataColumn oc in old.Columns)
                    {
                        string nc = MatchNewCol(ns, oc.ColumnName, newPeriod);
                        if (nc == null) continue;
                        nr[nc] = IsId(oc.ColumnName) ? or[oc] : (object)"0";
                    }

                    // Step 2: carry forward outstanding balance → Previous Quarter Pending
                    // Use the Total column if available (it includes quarterly + prev pending
                    // + all sub-fees). Fall back to quarterly + prevPending if no Total column.
                    decimal carry = 0m;
                    if (oldTotal != null)
                    {
                        decimal.TryParse(or[oldTotal]?.ToString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out carry);
                    }
                    else
                    {
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

                    // Step 2b: set Fine_Start_Date
                    // If the student has a carry-over balance, their fine has been
                    // accruing since the OLD quarter's start (or their own old
                    // Fine_Start_Date if they were already carrying from even earlier).
                    // If they were fully paid up, the new quarter's start applies.
                    if (carry > 0)
                    {
                        // Check if the old row itself had an earlier fine start date
                        // (happens when debt spans 3+ quarters).
                        string existingFineStart = old.Columns.Contains(FineStartCol)
                            ? or[FineStartCol]?.ToString()?.Trim() : null;

                        if (!string.IsNullOrEmpty(existingFineStart) &&
                            DateTime.TryParse(existingFineStart, out DateTime previousFineStart) &&
                            previousFineStart < oldQuarterStart)
                        {
                            // Debt is even older — keep the original start date.
                            nr[FineStartCol] = previousFineStart.ToString("yyyy-MM-dd");
                        }
                        else
                        {
                            // Fine starts from when the OLD quarter began.
                            nr[FineStartCol] = oldQuarterStart.ToString("yyyy-MM-dd");
                        }
                    }
                    else
                    {
                        // Student is paid up — fine resets to the new quarter's start.
                        nr[FineStartCol] = "";
                    }

                    // Step 3: restore quarterly fee for the new quarter
                    //   Free categories (SC, ST, *FW*) always get ₹0
                    if (oldQFee != null)
                    {
                        var nsQFee = FC(ns, "quarterly");
                        if (nsQFee != null)
                        {
                            string cat = oldCat != null
                                ? or[oldCat]?.ToString()?.Trim().ToUpper() ?? ""
                                : "";

                            bool freeCategory =
                                cat == "SC" || cat == "ST" || cat.Contains("FW");

                            if (freeCategory)
                            {
                                nr[nsQFee.ColumnName] = "0";
                            }
                            else
                            {
                                decimal.TryParse(or[oldQFee]?.ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out decimal oldQ);
                                nr[nsQFee.ColumnName] = oldQ.ToString("F2");
                            }
                        }
                    }

                    // Step 4: recalculate TOTAL = new quarterly + new previous pending
                    var nsTotalCol = FC(ns, "total", "fees") ?? FC(ns, "total");
                    if (nsTotalCol != null)
                    {
                        decimal newQAmt = 0m, newPrev = 0m;
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

                    // Step 5: reset fine for new quarter
                    if (nsFine != null) nr[nsFine.ColumnName] = "0";
                    var nsScholarship = FC(ns, "scholar");
                    if (nsScholarship != null) nr[nsScholarship.ColumnName] = "0";
                    ns.Rows.Add(nr);
                }

                string dept2 = ns.ExtendedProperties["Department"]?.ToString() ?? "";
                _csv.AddSheetToLoadedFiles(ns, dept2);

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
                    remarks: $"Auto-advanced {old.ExtendedProperties["Quarter"]} → {newQ}. " +
                             $"Sem {newSem}, Year {newYear}. {ns.Rows.Count} students.");

                return new TransitionResult
                {
                    OldSheet = old.TableName,
                    NewSheet = ns.TableName,
                    NewQuarter = newQ,
                    NewSemester = newSem,
                    NewYear = newYear,
                    StudentsCarried = ns.Rows.Count
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cycle] Advance failed for {old.TableName}: {ex.Message}");
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // LIVE FINE INJECTION  (call before displaying data in FeeCollection)
        // ════════════════════════════════════════════════════════════════════

        public void InjectLiveFines(DataTable sheet)
        {
            var start = PersistedStart(sheet.TableName);
            decimal fine = CalculateFine(start, DateTime.Now);
            if (fine <= 0) return;

            var fineCol = FC(sheet, "fine") ?? FC(sheet, "remarks");
            var nameCol = FC(sheet, "name");
            var pendCol = FC(sheet, "previous", "pending") ?? FC(sheet, "pending") ?? FC(sheet, "previous");
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

        // ════════════════════════════════════════════════════════════════════
        // COLUMN HELPERS
        // ════════════════════════════════════════════════════════════════════

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

        private static bool IsId(string col)
        {
            string n = col.ToLower();
            return n.Contains("name") || n.Contains("father") || n.Contains("mother")
                || n.Contains("categ") || n.Contains("section") || n.StartsWith("_")
                || n.Contains("hostel") || n.Contains("roll")    // NOTE: "scholar" intentionally removed — scholarship resets each quarter
                || n.Contains("sr no") || n.Contains("sr.")
                || n.Contains("stationary") || n.Contains("welfare")
                || n.Contains("insurance") || n.Contains("red cross")
                || n.Contains("student activ") || n.Contains("institutional")
                || n.Contains("refundable");
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
                @"\(?\s*(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|" +
                @"OCTOBER|NOVEMBER|DECEMBER|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)" +
                @"\s+\d{4}\s+[Tt][Oo]\s+" +
                @"(JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|" +
                @"OCTOBER|NOVEMBER|DECEMBER|JAN|FEB|MAR|APR|JUN|JUL|AUG|SEP|OCT|NOV|DEC)" +
                @"\s+\d{4}\s*\)?",
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

        // ════════════════════════════════════════════════════════════════════
        // PERIOD / YEAR STRINGS
        // ════════════════════════════════════════════════════════════════════

        private static string PeriodStr(string q)
        {
            int y = DateTime.Now.Year;
            return q switch
            {
                "Aug-Oct" => $"AUG {y} to OCT {y}",
                "Nov-Jan" => $"NOV {y} to JAN {y + 1}",
                "Feb-Apr" => $"FEB {y} to APRIL {y}",
                "May-Jul" => $"MAY {y} to JULY {y}",
                _ => $"{q} {y}",
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // STATE PERSISTENCE
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        // MISC HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static int GetSemester(DataTable t)
        {
            if (t.ExtendedProperties.ContainsKey("Semester") &&
                int.TryParse(t.ExtendedProperties["Semester"]?.ToString(), out int s) && s > 0)
                return s;
            return 1;
        }

        private static int GetYear(DataTable t)
        {
            if (t.ExtendedProperties.ContainsKey("Year") &&
                int.TryParse(t.ExtendedProperties["Year"]?.ToString(), out int y) && y > 0)
                return y;
            return 1;
        }

        // Maximum years for a department (override as needed)
        private static int MaxYears(string dept) => dept switch
        {
            "ME" => 4,   // Mechanical Engineering  — 4 years (Sem 1-8)
            "MECHATRONICS" => 3,   // Mechatronics Engineering — 3 years (Sem 1-6)
            "EE" => 3,   // Electrical Engineering   — 3 years (Sem 1-6)
            "CSE" => 3,   // Computer Science Engg    — 3 years (Sem 1-6)
            _ => 3,
        };

        // ════════════════════════════════════════════════════════════════════
        // VALUE TYPES
        // ════════════════════════════════════════════════════════════════════

        public class TransitionResult
        {
            public string OldSheet { get; set; }
            public string NewSheet { get; set; }
            public string NewQuarter { get; set; }
            public int NewSemester { get; set; }
            public int NewYear { get; set; }
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