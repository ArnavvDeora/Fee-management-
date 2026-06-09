using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using SchoolFeeSystem.Presentation; // <--- REQUIRED

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class HolidayManagementViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;

        [ObservableProperty] private ObservableCollection<Holiday> _holidays;
        [ObservableProperty] private DateTime _newHolidayDate = DateTime.Now;
        [ObservableProperty] private string _newHolidayName;
        [ObservableProperty] private int _selectedYear = DateTime.Now.Year;

        public HolidayManagementViewModel(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
            LoadHolidays();
        }

        [RelayCommand]
        public void LoadHolidays()
        {
            var list = _attendanceService.GetHolidays(SelectedYear);

            // UX IMPROVEMENT: Order by Date ascending so the calendar looks right
            var sortedList = list.OrderBy(h => h.Date).ToList();

            Holidays = new ObservableCollection<Holiday>(sortedList);
        }

        [RelayCommand]
        public void AddHoliday()
        {
            if (string.IsNullOrWhiteSpace(NewHolidayName)) return;

            var holiday = new Holiday { Date = NewHolidayDate, Name = NewHolidayName, IsRecurring = true };
            _attendanceService.AddHoliday(holiday);

            // ✅ HOLIDAY FIX: AddHoliday now auto-syncs attendance records for this date.
            // Any "Absent" records on this date are converted to "Holiday" status,
            // so salary calculations will count this as a paid day.

            NewHolidayName = "";
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
                $"Remove holiday '{holiday.Name}' on {holiday.Date:dd-MMM-yyyy}?\n\n" +
                "This will also revert any 'Holiday' attendance records on this date back to 'Absent'.\n" +
                "Salary calculations will be affected.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            _attendanceService.DeleteHoliday(holiday.Id);
            Holidays.Remove(holiday);
        }
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
            // FIX: Explicit cast
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}