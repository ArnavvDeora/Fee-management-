using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace SchoolFeeSystem.Presentation.Services
{
    /// <summary>
    /// Single source of truth for all fine calculations.
    ///
    /// FINE RULES (4-quarter academic year):
    ///   Quarter cycles: Feb–Apr | May–Jul | Aug–Oct | Nov–Jan
    ///   Quarter start  = 1st day of the first month in the cycle (e.g. 1 Feb).
    ///   Grace period   = days 1–15 of the first month → NO fine.
    ///
    ///   Month 1  (day 16 → end of first calendar month):
    ///            Rs 10 per day for every day past day 15.
    ///            e.g. 16 Feb = 1 day → Rs 10 | 28 Feb = 13 days → Rs 130
    ///
    ///   Month 2  (entire second calendar month of the quarter):
    ///            Rs 20 per day for every day in that month.
    ///            Month-1 fine is carried forward.
    ///
    ///   Month 3+ (from the third calendar month onwards):
    ///            Flat Rs 750 per month (each started month counts).
    ///            Month-1 and Month-2 fines are carried forward.
    ///
    /// Fine is ONLY applied to students who have outstanding fees
    /// (previous pending > 0 OR quarterly fees > 0).
    /// Students who have paid in full get fine = 0.
    /// </summary>
    public class FineCalculationService
    {
        // ── Fine rates ───────────────────────────────────────────────────────
        private const decimal Month1DailyRate = 10m;
        private const decimal Month2DailyRate = 20m;
        private const decimal Month3FlatRate = 1000m;
        private const int GraceDays = 15;

        // ── Quarter definitions (same as AcademicCycleService) ───────────────
        // Used for robust period-string parsing.
        private static readonly (string[] startAbbrs, int startMonth)[] QuarterStarts =
        {
            (new[]{"FEB","FEBRUARY"},  2),
            (new[]{"MAY"},             5),
            (new[]{"AUG","AUGUST"},    8),
            (new[]{"NOV","NOVEMBER"},  11),
        };

        // ═════════════════════════════════════════════════════════════════════
        // CORE CALCULATION
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the fine owed as of <paramref name="asOfDate"/>
        /// for a student whose quarter started on <paramref name="quarterStartDate"/>
        /// who has NOT yet paid.  Returns 0 if within the grace period.
        /// </summary>
        public decimal Calculate(DateTime quarterStartDate, DateTime asOfDate)
        {
            quarterStartDate = quarterStartDate.Date;
            asOfDate = asOfDate.Date;

            DateTime graceEnd = quarterStartDate.AddDays(GraceDays);  // e.g. 15 Feb
            if (asOfDate <= graceEnd)
                return 0m;

            // Calendar boundaries
            DateTime month1FineStart = graceEnd.AddDays(1);                               // 16 Feb
            DateTime month1End = EndOfMonth(quarterStartDate);                      // 28 Feb
            DateTime month2Start = month1End.AddDays(1);                              // 1 Mar
            DateTime month2End = EndOfMonth(month2Start);                           // 31 Mar
            DateTime month3Start = month2End.AddDays(1);                              // 1 Apr

            decimal fine = 0m;

            // Month-1 fine
            if (asOfDate >= month1FineStart)
            {
                DateTime cap = asOfDate < month1End ? asOfDate : month1End;
                int days = (cap - month1FineStart).Days + 1;
                fine += days * Month1DailyRate;
            }

            // Month-2 fine
            if (asOfDate >= month2Start)
            {
                DateTime cap = asOfDate < month2End ? asOfDate : month2End;
                int days = (cap - month2Start).Days + 1;
                fine += days * Month2DailyRate;
            }

            // Month-3+ flat fine
            if (asOfDate >= month3Start)
            {
                int extraMonths = MonthsDifference(month3Start, asOfDate) + 1;
                fine += extraMonths * Month3FlatRate;
            }

            return fine;
        }

        /// <summary>
        /// Returns a human-readable breakdown for the admin UI / tooltips.
        /// </summary>
        public FineBreakdown GetBreakdown(DateTime quarterStartDate, DateTime asOfDate)
        {
            quarterStartDate = quarterStartDate.Date;
            asOfDate = asOfDate.Date;

            var bd = new FineBreakdown
            {
                QuarterStart = quarterStartDate,
                AsOfDate = asOfDate,
                GraceEndDate = quarterStartDate.AddDays(GraceDays)
            };

            if (asOfDate <= bd.GraceEndDate)
            {
                bd.IsInGracePeriod = true;
                bd.TotalFine = 0m;
                bd.Summary = $"Within grace period (first {GraceDays} days). No fine.";
                return bd;
            }

            DateTime month1FineStart = bd.GraceEndDate.AddDays(1);
            DateTime month1End = EndOfMonth(quarterStartDate);
            DateTime month2Start = month1End.AddDays(1);
            DateTime month2End = EndOfMonth(month2Start);
            DateTime month3Start = month2End.AddDays(1);

            // Month-1
            if (asOfDate >= month1FineStart)
            {
                DateTime cap = asOfDate < month1End ? asOfDate : month1End;
                bd.Month1Days = (cap - month1FineStart).Days + 1;
                bd.Month1Fine = bd.Month1Days * Month1DailyRate;
            }

            // Month-2
            if (asOfDate >= month2Start)
            {
                DateTime cap = asOfDate < month2End ? asOfDate : month2End;
                bd.Month2Days = (cap - month2Start).Days + 1;
                bd.Month2Fine = bd.Month2Days * Month2DailyRate;
            }

            // Month-3+
            if (asOfDate >= month3Start)
            {
                bd.ExtraMonths = MonthsDifference(month3Start, asOfDate) + 1;
                bd.Month3Fine = bd.ExtraMonths * Month3FlatRate;
            }

            bd.TotalFine = bd.Month1Fine + bd.Month2Fine + bd.Month3Fine;
            bd.Summary =
                $"Grace period ended : {bd.GraceEndDate:dd-MM-yyyy}\n" +
                (bd.Month1Days > 0
                    ? $"Month-1 ({bd.Month1Days} days × Rs {Month1DailyRate}): Rs {bd.Month1Fine:N2}\n"
                    : "") +
                (bd.Month2Days > 0
                    ? $"Month-2 ({bd.Month2Days} days × Rs {Month2DailyRate}): Rs {bd.Month2Fine:N2}\n"
                    : "") +
                (bd.ExtraMonths > 0
                    ? $"Month-3+ ({bd.ExtraMonths} month(s) × Rs {Month3FlatRate}): Rs {bd.Month3Fine:N2}\n"
                    : "") +
                $"TOTAL FINE: Rs {bd.TotalFine:N2}";

            return bd;
        }

        // ═════════════════════════════════════════════════════════════════════
        // TABLE INJECTION  (called by FeeCollectionViewModel on every sheet load)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates today's live fine and writes it into the "Fine" column of
        /// every row in <paramref name="table"/>.
        ///
        /// Rules enforced here:
        ///   • Only students with Previous Pending > 0 OR Quarterly Fees > 0 get a fine.
        ///   • Students who have fully paid → Fine = "0.00".
        ///   • The Fine column is created if it does not already exist.
        ///
        /// Quarter start is resolved in this order:
        ///   1. <paramref name="overrideQuarterStart"/> parameter (from FeeCollectionViewModel)
        ///   2. table.ExtendedProperties["QuarterStart"]
        ///   3. Fallback: 1st day of the current calendar month
        /// </summary>
        public void InjectFinesIntoTable(DataTable table, DateTime? overrideQuarterStart = null)
        {
            if (table == null) return;

            // ── Resolve quarter start ─────────────────────────────────────────
            DateTime quarterStart;
            if (overrideQuarterStart.HasValue)
            {
                quarterStart = overrideQuarterStart.Value.Date;
                // Cache so BuildFineReport sees the same value
                table.ExtendedProperties["QuarterStart"] = quarterStart;
            }
            else if (table.ExtendedProperties.ContainsKey("QuarterStart") &&
                     table.ExtendedProperties["QuarterStart"] is DateTime qs)
            {
                quarterStart = qs;
            }
            else
            {
                quarterStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                table.ExtendedProperties["QuarterStart"] = quarterStart;
            }

            // ── Ensure Fine column exists ─────────────────────────────────────
            const string FineColName = "Fine";
            if (!table.Columns.Contains(FineColName))
                table.Columns.Add(FineColName, typeof(string));

            // ── Find relevant columns ─────────────────────────────────────────
            DataColumn prevCol = FindColAny(table, "previous pending", "previous");
            DataColumn qCol = FindColAny(table, "quarterly fees", "installment", "quarterly fee");
            DataColumn paidCol = FindColAny(table, "amount paid", "paid amount", "paid");
            DataColumn waiverCol = FindColAny(table, "fine waiver");
            // Fine_Start_Date: written by AcademicCycleService.Advance() for students
            // whose debt originated in an earlier quarter. When present, the fine
            // accrues from that earlier date rather than the current quarter start.
            DataColumn fineStartCol = FindColAny(table, "Fine_Start_Date");

            // ── Calculate today's gross fine for this quarter (default) ──────
            // Used for students who have no carry-over debt (fine starts fresh).
            decimal defaultGrossFine = Calculate(quarterStart, DateTime.Now.Date);

            // ── Write net fine into each student row ──────────────────────────
            foreach (DataRow row in table.Rows)
            {
                decimal prevAmt = ReadDec(row, prevCol);
                decimal qAmt = ReadDec(row, qCol);

                // Only students with an outstanding balance attract a fine.
                bool hasPending = prevAmt > 0 || qAmt > 0;
                if (!hasPending)
                {
                    row[FineColName] = "0.00";
                    continue;
                }

                // ── Determine effective fine start date ───────────────────────
                // If this student has a carry-over from a previous quarter, their
                // fine has been accruing since the original debt started — not from
                // the current quarter's grace-period end. One fine stream from the
                // earliest unpaid date covers everything (the month-3+ flat rate
                // already accounts for each month the debt remains unpaid).
                decimal grossFine;
                if (fineStartCol != null && prevAmt > 0)
                {
                    string fsd = row[fineStartCol]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(fsd) &&
                        DateTime.TryParse(fsd, out DateTime originalStart) &&
                        originalStart < quarterStart)
                    {
                        // Single fine stream from the original debt date
                        grossFine = Calculate(originalStart, DateTime.Now.Date);
                    }
                    else
                    {
                        // No valid earlier date — use the current quarter start.
                        grossFine = defaultGrossFine;
                    }
                }
                else
                {
                    // No carry-over — fine accrues from current quarter start.
                    grossFine = defaultGrossFine;
                }

                decimal waived = waiverCol != null ? ReadDec(row, waiverCol) : 0m;
                decimal netFine = Math.Max(0m, grossFine - waived);
                row[FineColName] = netFine.ToString("F2");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // FINE REPORT  (used by FineManagementViewModel)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans every sheet in <paramref name="csvService"/> and builds a
        /// DataTable showing every student's live fine status.
        /// </summary>
        public DataTable BuildFineReport(CsvDataService csvService)
        {
            var report = new DataTable("Fine Report");
            report.Columns.Add("Student Name");
            report.Columns.Add("Student ID");
            report.Columns.Add("Guardian");
            report.Columns.Add("Sheet / Class");
            report.Columns.Add("Quarter");
            report.Columns.Add("Previous Pending", typeof(decimal));
            report.Columns.Add("Quarterly Fees", typeof(decimal));
            report.Columns.Add("Waived Amount", typeof(decimal));  // NEW: shows admin waivers
            report.Columns.Add("Fine Amount", typeof(decimal));  // net fine after waiver
            report.Columns.Add("Total Due", typeof(decimal));
            report.Columns.Add("Status");
            report.Columns.Add("Fine Breakdown");

            foreach (var sheetName in csvService.GetSheetNames())
            {
                // Skip internal / history tables
                if (sheetName.ToLower().Contains("payment history") ||
                    sheetName.StartsWith("_")) continue;

                var table = csvService.GetSheet(sheetName);
                if (table == null) continue;

                // ── Determine quarter start for this sheet ────────────────────
                DateTime quarterStart;
                if (table.ExtendedProperties.ContainsKey("QuarterStart") &&
                    table.ExtendedProperties["QuarterStart"] is DateTime qs)
                {
                    quarterStart = qs;
                }
                else
                {
                    var meta = csvService.GetSheetMetadata(sheetName);
                    quarterStart = TryParseQuarterStart(meta?.Period)
                                   ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    table.ExtendedProperties["QuarterStart"] = quarterStart;
                }

                string quarter = table.ExtendedProperties["Period"]?.ToString()
                              ?? csvService.GetSheetMetadata(sheetName)?.Period
                              ?? sheetName;

                // ── Locate data columns ───────────────────────────────────────
                DataColumn nameCol = FindColAny(table, "name");
                DataColumn idCol = FindColAny(table, "student id", "roll no", "reg no", "roll", "reg");
                DataColumn guardianCol = FindColAny(table, "father name", "father", "guardian", "parent");
                DataColumn prevCol = FindColAny(table, "previous pending", "previous");
                DataColumn qCol = FindColAny(table, "quarterly fees", "installment", "quarterly fee");
                // Read persisted waivers so the report stays in sync with FeeCollection
                DataColumn waiverCol = FindColAny(table, "fine waiver");

                // ── Iterate students ──────────────────────────────────────────
                foreach (DataRow row in table.Rows)
                {
                    decimal prev = ReadDec(row, prevCol);
                    decimal q = ReadDec(row, qCol);

                    // Skip students with no outstanding balance
                    if (prev == 0 && q == 0) continue;

                    var bd = GetBreakdown(quarterStart, DateTime.Now.Date);
                    decimal gross = bd.TotalFine;
                    // Subtract any admin-granted waiver (same logic as InjectFinesIntoTable)
                    decimal waived = waiverCol != null ? ReadDec(row, waiverCol) : 0m;
                    decimal net = Math.Max(0m, gross - waived);

                    var r = report.NewRow();
                    r["Student Name"] = SafeStr(row, nameCol);
                    r["Student ID"] = SafeStr(row, idCol);
                    r["Guardian"] = SafeStr(row, guardianCol);
                    r["Sheet / Class"] = sheetName;
                    r["Quarter"] = quarter;
                    r["Previous Pending"] = prev;
                    r["Quarterly Fees"] = q;
                    r["Waived Amount"] = waived;
                    r["Fine Amount"] = net;
                    r["Total Due"] = prev + q + net;
                    r["Status"] = net > 0 ? "Fine Applicable" : "No Fine";
                    r["Fine Breakdown"] = bd.Summary.Replace("\n", " | ");
                    report.Rows.Add(r);
                }
            }

            return report;
        }

        // ═════════════════════════════════════════════════════════════════════
        // QUARTER-START PARSER
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parses the quarter start date from a period string.
        ///
        /// Handles all formats produced by the system:
        ///   "FEB 2026 to APRIL 2026"
        ///   "Feb 2026 to April 2026"
        ///   "FEBRUARY 2026 to APRIL 2026"
        ///   "AUG 2025 to OCT 2025"
        ///   "NOV 2025 to JAN 2026"
        ///   "MAY 2026 to JULY 2026"
        ///   Plain month-year: "FEB 2026"
        ///
        /// Returns null if the string cannot be parsed.
        /// </summary>
        public static DateTime? TryParseQuarterStart(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) return null;

            try
            {
                // Take the token BEFORE " to " (case-insensitive)
                string first = period
                    .Split(new[] { " to ", " TO ", " To " },
                           StringSplitOptions.RemoveEmptyEntries)[0]
                    .Trim();

                // Try multiple format patterns
                string[] formats =
                {
                    "MMM yyyy",       // FEB 2026
                    "MMMM yyyy",      // FEBRUARY 2026
                    "MMM-yyyy",       // FEB-2026
                    "MMMM-yyyy",      // FEBRUARY-2026
                    "MM/yyyy",        // 02/2026
                    "M/yyyy",         // 2/2026
                };

                var cultures = new[]
                {
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.CultureInfo.GetCultureInfo("en-IN"),
                    System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                };

                foreach (var fmt in formats)
                {
                    foreach (var culture in cultures)
                    {
                        if (DateTime.TryParseExact(first, fmt, culture,
                                System.Globalization.DateTimeStyles.None,
                                out DateTime result))
                            return new DateTime(result.Year, result.Month, 1);
                    }
                }

                // Fallback: match the start-month abbreviation against known quarter starts
                string upper = first.ToUpperInvariant();
                foreach (var (abbrs, startMonth) in QuarterStarts)
                {
                    foreach (var abbr in abbrs)
                    {
                        if (upper.StartsWith(abbr, StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract the year (4-digit number in the string)
                            var yearMatch = System.Text.RegularExpressions.Regex
                                .Match(first, @"\b(20\d{2})\b");
                            if (yearMatch.Success && int.TryParse(yearMatch.Value, out int yr))
                                return new DateTime(yr, startMonth, 1);
                        }
                    }
                }
            }
            catch { /* fall through */ }

            return null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private static DateTime EndOfMonth(DateTime d) =>
            new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

        private static int MonthsDifference(DateTime from, DateTime to) =>
            Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month);

        /// <summary>
        /// Finds the first DataColumn whose name contains ANY of the given keywords
        /// (case-insensitive).  Keywords are tried in order; first match wins.
        /// Multi-word keywords like "quarterly fees" are matched as a single phrase.
        /// </summary>
        private static DataColumn FindColAny(DataTable t, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                var col = t.Columns.Cast<DataColumn>()
                           .FirstOrDefault(c => c.ColumnName
                               .IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (col != null) return col;
            }
            return null;
        }

        // Keep old name as alias so existing callers compile unchanged
        private static DataColumn FindCol(DataTable t, params string[] keywords) =>
            FindColAny(t, keywords);

        private static decimal ReadDec(DataRow row, DataColumn col)
        {
            if (col == null) return 0m;
            string raw = row[col]?.ToString()?.Trim()
                             .Replace("₹", "").Replace(",", "") ?? "";
            return decimal.TryParse(raw, out decimal v) ? v : 0m;
        }

        private static string SafeStr(DataRow row, DataColumn col) =>
            col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    public class FineBreakdown
    {
        public DateTime QuarterStart { get; set; }
        public DateTime AsOfDate { get; set; }
        public DateTime GraceEndDate { get; set; }
        public bool IsInGracePeriod { get; set; }

        public int Month1Days { get; set; }
        public decimal Month1Fine { get; set; }

        public int Month2Days { get; set; }
        public decimal Month2Fine { get; set; }

        public int ExtraMonths { get; set; }
        public decimal Month3Fine { get; set; }

        public decimal TotalFine { get; set; }
        public string Summary { get; set; }
    }
}