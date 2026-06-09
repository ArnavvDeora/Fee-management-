using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System;
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

            // ===== FIX: Don't mutate _actualRecord directly =====
            // _actualRecord is an EF-tracked entity. If we zero its OT/penalty fields
            // here, AddOrUpdateAttendanceBatch can't read the OLD values to reverse
            // OT banking and allowance consumption. Instead, build a NEW record object
            // with the edited values and pass that. The batch method will find the
            // original tracked entity (old values intact), reverse side-effects, then
            // overwrite with the new values.

            // Calculate new duration from edited times
            string newDuration = "0h 0m";
            if (TimeSpan.TryParse(EditInTime?.Trim(), out var tIn) &&
                TimeSpan.TryParse(EditOutTime?.Trim(), out var tOut) &&
                tOut > TimeSpan.Zero)
            {
                var diff = tOut - tIn;
                if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromHours(24));
                newDuration = $"{(int)diff.TotalHours}h {diff.Minutes}m";
            }

            // Build a detached record with the edited values
            var updatedRecord = new AttendanceRecord
            {
                Id = _actualRecord.Id,
                EmployeeId = _actualRecord.EmployeeId,
                Date = _actualRecord.Date,
                InTime = EditInTime?.Trim() ?? "00:00",
                OutTime = EditOutTime?.Trim() ?? "00:00",
                Status = EditStatus,
                Duration = newDuration,
                Remarks = _actualRecord.Remarks,
                IsManualEntry = true,

                // Zeroed — CalculateOvertimeAndPenalties will set them fresh for Present
                LateMinutes = 0,
                LatePenaltyMinutes = 0,
                OvertimeMinutes = 0,
                AllowanceTimeUsed = 0
            };

            // MarkAttendance → AddOrUpdateAttendanceBatch finds the OLD tracked record
            // (with original OT/penalty values), reverses side-effects, THEN overwrites.
            _attendanceService.MarkAttendance(updatedRecord);

            // Close Window
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