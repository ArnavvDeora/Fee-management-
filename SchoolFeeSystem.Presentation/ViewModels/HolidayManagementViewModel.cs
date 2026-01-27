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

            NewHolidayName = "";
            LoadHolidays();
        }

        [RelayCommand]
        public void DeleteHoliday(Holiday holiday)
        {
            if (holiday == null) return;
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