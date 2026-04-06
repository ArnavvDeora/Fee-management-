using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Vml.Spreadsheet;
using SchoolFeeSystem.Presentation.Services;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterTimelineViewModel
    //
    //  Powers the "Quarter History" popup / side-panel that appears when the
    //  admin clicks "View History" on any course card.
    //
    //  What it shows
    //  ─────────────
    //  ┌─────────────────────────────────────────────────────────────┐
    //  │  Diploma ME – Year 2 (Sem 3 & 4)                           │
    //  │  Original file added: 12 Aug 2024                           │
    //  │  Current quarter: Feb-Apr 2026                              │
    //  │                                                             │
    //  │  ◉ Aug-Oct 2024  Sem 3  38 students  30 paid / 8 pending   │
    //  │  ○ Nov-Jan 2025  Sem 3  38 students  35 paid / 3 pending   │
    //  │  ○ Feb-Apr 2025  Sem 4  38 students  38 paid / 0 pending   │
    //  │  ● Feb-Apr 2026  Sem 4  (current)                          │
    //  │                                     [View Selected Quarter] │
    //  └─────────────────────────────────────────────────────────────┘
    //
    //  Selecting a past entry and clicking "View" loads the read-only snapshot
    //  DataTable into CsvTableView on the parent ClassViewModel so the existing
    //  DataGrid can display it — with a banner making clear it is historical.
    // ══════════════════════════════════════════════════════════════════════════

    public partial class QuarterTimelineViewModel : ObservableObject
    {
        // ── Dependencies ────────────────────────────────────────────────────
        private readonly QuarterHistoryService _history;
        private readonly AcademicCycleService _cycle;

        // ── Parent sheet context ────────────────────────────────────────────
        private DataTable _liveSheet;

        // ════════════════════════════════════════════════════════════════════
        // OBSERVABLE PROPERTIES
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Display title, e.g. "Diploma ME – Year 2".</summary>
        [ObservableProperty]
        private string courseTitle = "";

        /// <summary>"Original file added on 12 Aug 2024" text.</summary>
        [ObservableProperty]
        private string originalFileAddedText = "";

        /// <summary>"Current quarter: Feb-Apr 2026 (Sem 4)" text.</summary>
        [ObservableProperty]
        private string currentQuarterText = "";

        /// <summary>All history entries for this class (sorted oldest→newest).</summary>
        public ObservableCollection<QuarterEntryVM> Entries { get; } = new();

        /// <summary>The entry the admin has highlighted on the timeline.</summary>
        [ObservableProperty]
        private QuarterEntryVM selectedEntry;

        /// <summary>True while the history panel should be visible.</summary>
        [ObservableProperty]
        private bool isVisible = false;

        /// <summary>
        /// After "View" is clicked this holds the snapshot DataTable to display.
        /// Null = display live data.
        /// </summary>
        [ObservableProperty]
        private DataTable activeSnapshot;

        /// <summary>
        /// Banner text shown above the DataGrid when a historical snapshot is open.
        /// </summary>
        [ObservableProperty]
        private string snapshotBanner = "";

        /// <summary>True when a historical (read-only) snapshot is being shown.</summary>
        [ObservableProperty]
        private bool isShowingSnapshot = false;

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
        // PUBLIC API — called by ClassViewModel
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the timeline panel for the given sheet (course).
        /// Call when the user clicks "View History" on a course card.
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
            CurrentQuarterText = $"Current quarter: {curQ} {DateTime.Now.Year}  (Sem {curSem})";

            // ── History entries ────────────────────────────────────────────
            Entries.Clear();
            var history = _history.GetHistory(sheet);
            string sheetKey = QuarterHistoryService.SheetKey(sheet);

            foreach (var entry in history)
            {
                bool isCurrent = entry.Quarter == curQ
                              && entry.CalendarYear == DateTime.Now.Year;

                Entries.Add(new QuarterEntryVM
                {
                    Entry = entry,
                    QuarterLabel = entry.QuarterLabel,
                    SemLabel = entry.SemesterLabel,
                    TotalText = $"{entry.TotalStudents} students",
                    PaidText = $"{entry.PaidStudents} paid",
                    PendingText = entry.PendingStudents > 0
                                    ? $"🔴 {entry.PendingStudents} pending"
                                    : "✅ All paid",
                    IsCurrent = isCurrent,
                    SnapshotDate = isCurrent
                                    ? "Live"
                                    : $"Snapshotted {entry.SnapshotTaken:dd MMM yyyy}",
                });
            }

            // Append a "Live" row for the current quarter if not already in history
            if (!history.Any(e => e.Quarter == curQ &&
                                  e.CalendarYear == DateTime.Now.Year))
            {
                Entries.Add(new QuarterEntryVM
                {
                    Entry = null,
                    QuarterLabel = $"{curQ} {DateTime.Now.Year}",
                    SemLabel = $"Sem {curSem}",
                    TotalText = $"{CountStudents(sheet)} students",
                    PaidText = "",
                    PendingText = "(Live — data not yet snapshotted)",
                    IsCurrent = true,
                    SnapshotDate = "Live",
                });
            }

            SelectedEntry = Entries.LastOrDefault();
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

        /// <summary>Loads the selected quarter's snapshot into ActiveSnapshot.</summary>
        [RelayCommand]
        public void ViewSelectedQuarter()
        {
            if (SelectedEntry == null) return;

            // Current / live entry → show live data
            if (SelectedEntry.IsCurrent || SelectedEntry.Entry == null)
            {
                ActiveSnapshot = null;
                IsShowingSnapshot = false;
                SnapshotBanner = "";
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

        /// <summary>Return to live (current quarter) data.</summary>
        [RelayCommand]
        public void ReturnToLive()
        {
            ActiveSnapshot = null;
            IsShowingSnapshot = false;
            SnapshotBanner = "";

            // Re-select the live entry in the list
            var live = Entries.FirstOrDefault(e => e.IsCurrent);
            if (live != null) SelectedEntry = live;
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  QuarterEntryVM — one row in the timeline list
    // ══════════════════════════════════════════════════════════════════════════

    public class QuarterEntryVM : ObservableObject
    {
        /// <summary>Underlying data-entry; null for the live row.</summary>
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