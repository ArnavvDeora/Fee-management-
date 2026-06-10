using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    // =========================================================
    // CALENDAR DAY — one cell in the monthly calendar grid
    // =========================================================
    public class CalendarDay : ObservableObject
    {
        public int Day { get; set; }
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }
        public bool IsWeekend { get; set; }
        public bool IsHoliday { get; set; }
        public string HolidayName { get; set; }

        // Visual helpers for XAML binding
        public string DayText => IsCurrentMonth ? Day.ToString() : "";
        public string ToolTip => IsHoliday ? HolidayName : null;
    }

    // =========================================================
    // VIEW MODEL
    // =========================================================
    public partial class HolidayManagementViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;

        // --- Holiday data ---
        [ObservableProperty] private ObservableCollection<Holiday> _holidays;
        [ObservableProperty] private DateTime _newHolidayDate = DateTime.Now;
        [ObservableProperty] private string _newHolidayName;
        [ObservableProperty] private int _selectedYear = DateTime.Now.Year;

        // --- Calendar grid ---
        [ObservableProperty] private int _calendarMonth = DateTime.Now.Month;
        [ObservableProperty] private ObservableCollection<CalendarDay> _calendarDays = new();
        [ObservableProperty] private string _currentMonthDisplay = "";

        // --- Stats ---
        [ObservableProperty] private int _totalHolidaysThisYear;
        [ObservableProperty] private int _holidaysThisMonth;
        [ObservableProperty] private string _nextHolidayText = "None upcoming";
        [ObservableProperty] private int _holidaysRemainingThisYear;

        // --- Holidays visible in the month panel (filtered) ---
        [ObservableProperty] private ObservableCollection<Holiday> _monthHolidays = new();

        public HolidayManagementViewModel(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
            LoadHolidays();
        }

        // =========================================================
        // CORE DATA LOAD
        // =========================================================

        [RelayCommand]
        public void LoadHolidays()
        {
            var list = _attendanceService.GetHolidays(SelectedYear);
            var sortedList = list.OrderBy(h => h.Date).ToList();
            Holidays = new ObservableCollection<Holiday>(sortedList);

            RefreshCalendar();
            RefreshStats();
        }

        // =========================================================
        // CALENDAR GRID
        // =========================================================

        private void RefreshCalendar()
        {
            var culture = new CultureInfo("en-IN");
            CurrentMonthDisplay = new DateTime(SelectedYear, CalendarMonth, 1).ToString("MMMM yyyy", culture);

            var days = new ObservableCollection<CalendarDay>();
            var firstDay = new DateTime(SelectedYear, CalendarMonth, 1);
            int daysInMonth = DateTime.DaysInMonth(SelectedYear, CalendarMonth);

            // Monday=0 ... Sunday=6
            int startOffset = ((int)firstDay.DayOfWeek + 6) % 7;

            var holidayDates = (Holidays ?? new ObservableCollection<Holiday>())
                .Where(h => h.Date.Month == CalendarMonth && h.Date.Year == SelectedYear)
                .ToDictionary(h => h.Date.Date, h => h.Name);

            // Previous month padding
            var prevMonth = firstDay.AddMonths(-1);
            int prevDays = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            for (int i = startOffset - 1; i >= 0; i--)
            {
                int d = prevDays - i;
                days.Add(new CalendarDay
                {
                    Day = d,
                    Date = new DateTime(prevMonth.Year, prevMonth.Month, d),
                    IsCurrentMonth = false
                });
            }

            // Current month days
            var today = DateTime.Today;
            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(SelectedYear, CalendarMonth, d);
                bool isWeekend = date.DayOfWeek == DayOfWeek.Sunday;
                bool isHoliday = holidayDates.ContainsKey(date.Date);

                days.Add(new CalendarDay
                {
                    Day = d,
                    Date = date,
                    IsCurrentMonth = true,
                    IsToday = date == today,
                    IsWeekend = isWeekend,
                    IsHoliday = isHoliday,
                    HolidayName = isHoliday ? holidayDates[date.Date] : null
                });
            }

            // Fill to 42 cells (6 rows × 7 cols)
            int remaining = 42 - days.Count;
            for (int d = 1; d <= remaining; d++)
            {
                var nextMonth = firstDay.AddMonths(1);
                days.Add(new CalendarDay
                {
                    Day = d,
                    Date = new DateTime(nextMonth.Year, nextMonth.Month, d),
                    IsCurrentMonth = false
                });
            }

            CalendarDays = days;

            MonthHolidays = new ObservableCollection<Holiday>(
                (Holidays ?? new ObservableCollection<Holiday>())
                    .Where(h => h.Date.Month == CalendarMonth && h.Date.Year == SelectedYear)
                    .OrderBy(h => h.Date));
        }

        // =========================================================
        // STATS
        // =========================================================

        private void RefreshStats()
        {
            var allHolidays = Holidays?.ToList() ?? new System.Collections.Generic.List<Holiday>();
            TotalHolidaysThisYear = allHolidays.Count;
            HolidaysThisMonth = allHolidays.Count(h => h.Date.Month == CalendarMonth);
            HolidaysRemainingThisYear = allHolidays.Count(h => h.Date >= DateTime.Today);

            var next = allHolidays
                .Where(h => h.Date >= DateTime.Today)
                .OrderBy(h => h.Date)
                .FirstOrDefault();

            if (next != null)
            {
                int daysUntil = (next.Date.Date - DateTime.Today).Days;
                NextHolidayText = daysUntil == 0
                    ? $"Today — {next.Name}"
                    : daysUntil == 1
                        ? $"Tomorrow — {next.Name}"
                        : $"In {daysUntil} days — {next.Name}";
            }
            else
            {
                NextHolidayText = "No upcoming holidays";
            }
        }

        // =========================================================
        // MONTH NAVIGATION
        // =========================================================

        [RelayCommand]
        public void PreviousMonth()
        {
            if (CalendarMonth == 1)
            {
                CalendarMonth = 12;
                SelectedYear--;
                LoadHolidays();
            }
            else
            {
                CalendarMonth--;
                RefreshCalendar();
                RefreshStats();
            }
        }

        [RelayCommand]
        public void NextMonth()
        {
            if (CalendarMonth == 12)
            {
                CalendarMonth = 1;
                SelectedYear++;
                LoadHolidays();
            }
            else
            {
                CalendarMonth++;
                RefreshCalendar();
                RefreshStats();
            }
        }

        [RelayCommand]
        public void GoToToday()
        {
            SelectedYear = DateTime.Now.Year;
            CalendarMonth = DateTime.Now.Month;
            LoadHolidays();
        }

        // =========================================================
        // ADD / DELETE HOLIDAYS
        // =========================================================

        [RelayCommand]
        public void AddHoliday()
        {
            if (string.IsNullOrWhiteSpace(NewHolidayName)) return;

            var holiday = new Holiday { Date = NewHolidayDate, Name = NewHolidayName, IsRecurring = true };
            _attendanceService.AddHoliday(holiday);

            NewHolidayName = "";
            CalendarMonth = holiday.Date.Month;
            SelectedYear = holiday.Date.Year;
            LoadHolidays();

            MessageBox.Show(
                $"Holiday '{holiday.Name}' added on {holiday.Date:dd-MMM-yyyy}.\n\n" +
                "Attendance records have been automatically updated.\n" +
                "Any employee marked absent on this date will now show as Holiday.",
                "Holiday Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void DeleteHoliday(Holiday holiday)
        {
            if (holiday == null) return;

            var confirm = MessageBox.Show(
                $"Remove '{holiday.Name}' on {holiday.Date:dd-MMM-yyyy}?\n\n" +
                "Attendance records on this date will revert to Absent.\n" +
                "Salary calculations will be affected.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            _attendanceService.DeleteHoliday(holiday.Id);
            LoadHolidays();
        }

        // =========================================================
        // NAVIGATION
        // =========================================================

        [RelayCommand]
        public void GoToImport()
        {
            var services = ((App)Application.Current).Services;
            var importView = services.GetRequiredService<ImportHolidaysView>();
            Application.Current.MainWindow.Content = importView;
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}