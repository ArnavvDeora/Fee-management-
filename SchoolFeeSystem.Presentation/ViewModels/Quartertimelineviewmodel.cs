using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Presentation.Services;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterTimelineViewModel  (Fixed)
    //
    //  What changed vs original:
    //  ─────────────────────────
    //  1. Open() no longer appends a synthetic "Live" row when the current
    //     quarter is already in the history index (it always is now because
    //     AcademicCycleService snapshots the live quarter on every startup).
    //     Instead it marks the matching HistoryEntry as IsCurrent = true and
    //     labels it "Live" so the user sees it highlighted.
    //
    //  2. ViewSelectedQuarter() treats entries where IsCurrent = true as the
    //     live view (same behaviour as before), so clicking the live row shows
    //     the real-time DataTable, not the snapshot.
    //
    //  3. The "Back to Live" button always works because SelectedEntry is set
    //     to the live entry when the panel opens.
    // ══════════════════════════════════════════════════════════════════════════

    public partial class QuarterTimelineViewModel : ObservableObject
    {
        private readonly QuarterHistoryService _history;
        private readonly AcademicCycleService _cycle;

        private DataTable _liveSheet;

        // ════════════════════════════════════════════════════════════════════
        // OBSERVABLE PROPERTIES
        // ════════════════════════════════════════════════════════════════════

        [ObservableProperty] private string courseTitle = "";
        [ObservableProperty] private string originalFileAddedText = "";
        [ObservableProperty] private string currentQuarterText = "";

        public ObservableCollection<QuarterEntryVM> Entries { get; } = new();

        // Suppresses auto-load while Open()/ReturnToLive() programmatically
        // change SelectedEntry. Without this, opening the panel would load
        // the snapshot for whatever entry happens to be selected first.
        private bool _suppressAutoLoad;

        private QuarterEntryVM _selectedEntry;
        public QuarterEntryVM SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (!SetProperty(ref _selectedEntry, value)) return;

                // FIX: clicking a row in the timeline ListBox now auto-loads
                // that quarter's snapshot (or returns to live if the live row
                // was clicked). Previously the user had to press the
                // "📂 View Selected" button — clicks felt unresponsive.
                if (_suppressAutoLoad) return;
                if (value == null) return;

                ViewSelectedQuarter();
            }
        }

        [ObservableProperty] private bool isVisible = false;
        [ObservableProperty] private DataTable activeSnapshot;
        [ObservableProperty] private string snapshotBanner = "";
        [ObservableProperty] private bool isShowingSnapshot = false;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════

        public QuarterTimelineViewModel(QuarterHistoryService history,
                                         AcademicCycleService cycle)
        {
            _history = history;
            _cycle = cycle;
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the timeline panel for the given sheet (course).
        /// </summary>
        public void Open(DataTable sheet, string displayTitle)
        {
            _liveSheet = sheet;

            // ── Header texts ───────────────────────────────────────────────
            CourseTitle = displayTitle;

            DateTime importedOn = _cycle.GetOriginalImportDate(sheet.TableName);
            OriginalFileAddedText = importedOn == DateTime.MinValue
                ? "Original file: date unknown"
                : $"Original file added: {importedOn:dd MMM yyyy}";

            string curQ = AcademicCycleService.CurrentQuarter();
            int curSem = GetSemester(sheet);
            int curCalYear = DateTime.Now.Year;
            CurrentQuarterText = $"Current quarter: {curQ} {curCalYear}  (Sem {curSem})";

            // ── Build timeline ─────────────────────────────────────────────
            Entries.Clear();
            var history = _history.GetHistory(sheet);

            foreach (var entry in history)
            {
                // FIX: An entry is "live" when it matches the current real-world quarter
                // AND calendar year.  We show it differently but it IS in the index.
                bool isCurrent = entry.Quarter == curQ
                              && entry.CalendarYear == curCalYear;

                // Recalculate live stats from the real DataTable so they're always fresh
                int liveTotal = isCurrent ? CountStudents(sheet) : entry.TotalStudents;
                int livePaid = isCurrent ? CountPaid(sheet) : entry.PaidStudents;
                int livePending = liveTotal - livePaid;

                Entries.Add(new QuarterEntryVM
                {
                    Entry = entry,
                    QuarterLabel = entry.QuarterLabel,
                    SemLabel = entry.SemesterLabel,
                    TotalText = $"{liveTotal} students",
                    PaidText = $"{livePaid} paid",
                    PendingText = isCurrent
                                    ? (livePending > 0
                                        ? $"🔴 {livePending} pending"
                                        : "✅ All paid")
                                    : (entry.PendingStudents > 0
                                        ? $"🔴 {entry.PendingStudents} pending"
                                        : "✅ All paid"),
                    IsCurrent = isCurrent,
                    SnapshotDate = isCurrent
                                    ? "Live"
                                    : $"Snapshotted {entry.SnapshotTaken:dd MMM yyyy}",
                });
            }

            // Fallback: if for some reason the current quarter was never snapshotted,
            // append a synthetic live row so the panel is never empty.
            if (!Entries.Any(e => e.IsCurrent))
            {
                int tot = CountStudents(sheet);
                int paid = CountPaid(sheet);
                Entries.Add(new QuarterEntryVM
                {
                    Entry = null,
                    QuarterLabel = $"{curQ} {curCalYear}",
                    SemLabel = $"Sem {curSem}",
                    TotalText = $"{tot} students",
                    PaidText = $"{paid} paid",
                    PendingText = "(Live — not yet snapshotted)",
                    IsCurrent = true,
                    SnapshotDate = "Live",
                });
            }

            // ── Default selection: the live (most recent) entry ────────────
            // Suppress auto-load — opening the panel should show LIVE data,
            // not auto-load a snapshot just because a row got selected.
            _suppressAutoLoad = true;
            try
            {
                SelectedEntry = Entries.LastOrDefault(e => e.IsCurrent) ?? Entries.LastOrDefault();
            }
            finally
            {
                _suppressAutoLoad = false;
            }
            ActiveSnapshot = null;
            IsShowingSnapshot = false;
            SnapshotBanner = "";
            IsVisible = true;
        }

        /// <summary>Closes the timeline panel and returns to live data.</summary>
        [RelayCommand]
        public void Close()
        {
            IsVisible = false;
            ActiveSnapshot = null;
            IsShowingSnapshot = false;
            SnapshotBanner = "";
        }

        /// <summary>
        /// Loads the selected quarter's snapshot.
        /// If the selected entry is the live quarter → show the live DataTable.
        /// </summary>
        [RelayCommand]
        public void ViewSelectedQuarter()
        {
            if (SelectedEntry == null) return;

            // Live entry → always show real-time data, not the archived CSV
            if (SelectedEntry.IsCurrent)
            {
                ActiveSnapshot = null;
                IsShowingSnapshot = false;
                SnapshotBanner = "";
                return;
            }

            // Past entry → load snapshot
            if (SelectedEntry.Entry == null)
            {
                MessageBox.Show("This entry has no snapshot data.",
                    "No Snapshot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var snap = _history.LoadSnapshot(SelectedEntry.Entry);
            if (snap == null)
            {
                MessageBox.Show(
                    $"Snapshot data for {SelectedEntry.QuarterLabel} could not be found.\n" +
                    $"It may have been archived before the history feature was installed.",
                    "Snapshot Unavailable",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActiveSnapshot = snap;
            IsShowingSnapshot = true;
            SnapshotBanner =
                $"📅  Viewing archived data — {SelectedEntry.QuarterLabel}  " +
                $"({SelectedEntry.SemLabel})   |   " +
                $"Snapshotted on {SelectedEntry.Entry.SnapshotTaken:dd MMM yyyy HH:mm}   " +
                $"|   This is read-only.";
        }

        /// <summary>Returns to the live (current quarter) data.</summary>
        [RelayCommand]
        public void ReturnToLive()
        {
            ActiveSnapshot = null;
            IsShowingSnapshot = false;
            SnapshotBanner = "";

            var live = Entries.FirstOrDefault(e => e.IsCurrent);
            if (live != null)
            {
                // Suppress auto-load — we already cleared the snapshot above,
                // we don't want the setter to immediately try to re-load.
                _suppressAutoLoad = true;
                try { SelectedEntry = live; }
                finally { _suppressAutoLoad = false; }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static int GetSemester(DataTable t)
        {
            if (t.ExtendedProperties.ContainsKey("Semester") &&
                int.TryParse(t.ExtendedProperties["Semester"]?.ToString(), out int s) && s > 0)
                return s;
            return 1;
        }

        private static int CountStudents(DataTable t)
        {
            var nameCol = t.Columns.Cast<System.Data.DataColumn>()
                           .FirstOrDefault(c => c.ColumnName.IndexOf("name",
                               StringComparison.OrdinalIgnoreCase) >= 0);
            if (nameCol == null) return t.Rows.Count;
            return t.Rows.Cast<DataRow>()
                    .Count(r => {
                        var s = r[nameCol]?.ToString()?.Trim() ?? "";
                        return !string.IsNullOrEmpty(s) && s.Length <= 60
                            && !s.Equals("Name", StringComparison.OrdinalIgnoreCase)
                            && !s.StartsWith("Note", StringComparison.OrdinalIgnoreCase);
                    });
        }

        private static int CountPaid(DataTable t)
        {
            var totalCol = t.Columns.Cast<System.Data.DataColumn>()
                            .FirstOrDefault(c =>
                                c.ColumnName.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                c.ColumnName.IndexOf("fees", StringComparison.OrdinalIgnoreCase) >= 0);
            if (totalCol == null) return 0;

            var nameCol = t.Columns.Cast<System.Data.DataColumn>()
                           .FirstOrDefault(c => c.ColumnName.IndexOf("name",
                               StringComparison.OrdinalIgnoreCase) >= 0);

            return t.Rows.Cast<DataRow>()
                    .Count(r => {
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterEntryVM — one row in the timeline list
    // ══════════════════════════════════════════════════════════════════════════

    public class QuarterEntryVM : ObservableObject
    {
        /// <summary>Underlying data-entry; null only for synthetic live rows.</summary>
        public QuarterHistoryService.HistoryEntry Entry { get; set; }

        public string QuarterLabel { get; set; }
        public string SemLabel { get; set; }
        public string TotalText { get; set; }
        public string PaidText { get; set; }
        public string PendingText { get; set; }
        public string SnapshotDate { get; set; }

        /// <summary>True for the live (current) quarter row.</summary>
        public bool IsCurrent { get; set; }

        // Visual helpers for XAML
        public string DotIcon => IsCurrent ? "🔵" : "⚫";
        public string TimelineIcon => IsCurrent ? "●" : "○";
        public string RowOpacity => IsCurrent ? "1.0" : "0.85";
    }
}