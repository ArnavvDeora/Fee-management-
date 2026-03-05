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
            _actualRecord.InTime = EditInTime?.Trim();
            _actualRecord.OutTime = EditOutTime?.Trim();
            _actualRecord.Status = EditStatus;

            // FIX 1: Recalculate Duration from the new InTime/OutTime
            // Without this, the attendance grid shows the old duration after editing.
            if (TimeSpan.TryParse(_actualRecord.InTime, out var tIn) &&
                TimeSpan.TryParse(_actualRecord.OutTime, out var tOut) &&
                tOut > TimeSpan.Zero)
            {
                var diff = tOut - tIn;
                if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromHours(24));
                _actualRecord.Duration = $"{(int)diff.TotalHours}h {diff.Minutes}m";
            }
            else
            {
                _actualRecord.Duration = "0h 0m";
            }

            // FIX 2: Reset penalty/OT fields before recalculation so stale values
            // don't carry over. e.g. admin removes lateness — AllowanceTimeUsed must
            // reset to 0 or OvertimeCalc will apply the old offset incorrectly.
            _actualRecord.LateMinutes = 0;
            _actualRecord.LatePenaltyMinutes = 0;
            _actualRecord.OvertimeMinutes = 0;
            _actualRecord.AllowanceTimeUsed = 0;

            // 3. Update Database (MarkAttendance → AddOrUpdateAttendanceBatch
            //    → CalculateOvertimeAndPenalties recalculates everything fresh)
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