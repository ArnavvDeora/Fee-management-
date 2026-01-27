using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class EditAttendanceViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;
        private AttendanceRecord _actualRecord;

        // EDITABLE FIELDS (Temporary buffers)
        [ObservableProperty] private string _employeeName;
        [ObservableProperty] private string _dateDisplay;
        [ObservableProperty] private string _editInTime;
        [ObservableProperty] private string _editOutTime;
        [ObservableProperty] private string _editStatus;
        [ObservableProperty] private ObservableCollection<string> _statusOptions;

        public EditAttendanceViewModel(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
            StatusOptions = new ObservableCollection<string> { "Present", "Absent", "Leave", "Half Day", "Holiday" };
        }

        public void SetRecord(AttendanceRecord record)
        {
            _actualRecord = record;

            // 1. Copy values to temporary fields (Buffer)
            EmployeeName = record.Employee?.FullName ?? "Unknown";
            DateDisplay = record.Date.ToString("dd MMM yyyy");

            EditInTime = record.InTime;
            EditOutTime = record.OutTime;
            EditStatus = record.Status;
        }

        [RelayCommand]
        public void SaveChanges(Window window)
        {
            if (_actualRecord == null) return;

            // 2. Commit changes ONLY when Save is clicked
            _actualRecord.InTime = EditInTime;
            _actualRecord.OutTime = EditOutTime;
            _actualRecord.Status = EditStatus;

            // 3. Update Database
            _attendanceService.MarkAttendance(_actualRecord);

            // 4. Close Window
            if (window != null)
            {
                window.DialogResult = true; // Returns "True" to the parent to trigger refresh
                window.Close();
            }
        }

        [RelayCommand]
        public void Cancel(Window window)
        {
            // Close without saving
            if (window != null)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}