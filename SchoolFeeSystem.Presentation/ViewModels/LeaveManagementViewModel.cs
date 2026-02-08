using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class LeaveManagementViewModel : ObservableObject
    {
        private readonly ILeaveService _leaveService;
        private readonly IPayrollService _payrollService;

        [ObservableProperty]
        private ObservableCollection<Employee> _employees;

        [ObservableProperty]
        private Employee _selectedEmployee;

        [ObservableProperty]
        private DateTime _leaveDate = DateTime.Today;

        [ObservableProperty]
        private string _selectedLeaveType = "Half Day";

        [ObservableProperty]
        private string _startTime = "09:00";

        [ObservableProperty]
        private string _endTime = "13:00";

        [ObservableProperty]
        private decimal _customHours = 4;

        [ObservableProperty]
        private string _reason = string.Empty;

        [ObservableProperty]
        private string _remarks = string.Empty;

        [ObservableProperty]
        private ObservableCollection<LeaveRequest> _leaveHistory;

        [ObservableProperty]
        private LeaveRequest _selectedLeaveRecord;

        [ObservableProperty]
        private string _statusMessage = "Ready to grant leave";

        [ObservableProperty]
        private decimal _availableAllowanceHours = 0;

        [ObservableProperty]
        private bool _hasAllowanceTime = false;

        [ObservableProperty]
        private string _leaveSourcePreview = "Will use Allowance Time";

        public List<string> LeaveTypes { get; } = new List<string>
        {
            "Full Day",
            "Half Day",
            "Custom Hours"
        };

        public LeaveManagementViewModel(ILeaveService leaveService, IPayrollService payrollService)
        {
            _leaveService = leaveService;
            _payrollService = payrollService;

            LoadEmployees();
        }

        private void LoadEmployees()
        {
            var list = _payrollService.GetAllEmployees();
            Employees = new ObservableCollection<Employee>(list);

            if (Employees.Count > 0)
            {
                SelectedEmployee = Employees[0];
            }
        }

        partial void OnSelectedEmployeeChanged(Employee value)
        {
            if (value != null)
            {
                UpdateAllowanceInfo();
                LoadLeaveHistory();
            }
        }

        partial void OnSelectedLeaveTypeChanged(string value)
        {
            // Update default values based on leave type
            switch (value)
            {
                case "Full Day":
                    CustomHours = 8;
                    StartTime = "09:00";
                    EndTime = "17:00";
                    break;

                case "Half Day":
                    CustomHours = 4;
                    StartTime = "09:00";
                    EndTime = "13:00";
                    break;

                case "Custom Hours":
                    // Keep current values
                    break;
            }

            UpdateLeaveSourcePreview();
        }

        [RelayCommand]
        public void UpdateAllowanceInfo()
        {
            if (SelectedEmployee == null) return;

            var allowance = _payrollService.GetOvertimeAllowance(SelectedEmployee.Id);
            AvailableAllowanceHours = Math.Round(allowance.AvailableMinutes / 60.0m, 2);
            HasAllowanceTime = allowance.AvailableMinutes > 0;

            UpdateLeaveSourcePreview();
        }

        private void UpdateLeaveSourcePreview()
        {
            if (SelectedEmployee == null)
            {
                LeaveSourcePreview = "Select an employee";
                return;
            }

            decimal leaveHours = SelectedLeaveType == "Full Day" ? 8 :
                               SelectedLeaveType == "Half Day" ? 4 :
                               CustomHours;

            if (AvailableAllowanceHours >= leaveHours)
            {
                LeaveSourcePreview = $"✅ Will use {leaveHours} hrs from Allowance Time (Available: {AvailableAllowanceHours} hrs)";
            }
            else if (AvailableAllowanceHours > 0)
            {
                decimal unpaidHours = leaveHours - AvailableAllowanceHours;
                LeaveSourcePreview = $"⚠️ Partial: {AvailableAllowanceHours} hrs from Allowance + {unpaidHours} hrs Unpaid";
            }
            else
            {
                LeaveSourcePreview = $"❌ No allowance time. Leave will be UNPAID ({leaveHours} hrs deducted from salary)";
            }
        }

        [RelayCommand]
        public void CalculateCustomHours()
        {
            if (TimeSpan.TryParse(StartTime, out var start) &&
                TimeSpan.TryParse(EndTime, out var end))
            {
                CustomHours = (decimal)(end - start).TotalHours;
                UpdateLeaveSourcePreview();
            }
        }

        [RelayCommand]
        public void GrantLeave()
        {
            try
            {
                if (SelectedEmployee == null)
                {
                    MessageBox.Show("Please select an employee.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(Reason))
                {
                    MessageBox.Show("Please enter a reason for leave.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validate custom hours
                if (SelectedLeaveType == "Custom Hours")
                {
                    if (TimeSpan.TryParse(StartTime, out var start) &&
                        TimeSpan.TryParse(EndTime, out var end))
                    {
                        if (end <= start)
                        {
                            MessageBox.Show("End time must be after start time.", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Invalid time format. Use HH:mm format (e.g., 09:00).", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Create leave request
                var leaveRequest = new LeaveRequest
                {
                    EmployeeId = SelectedEmployee.Id,
                    LeaveDate = LeaveDate,
                    LeaveType = SelectedLeaveType,
                    StartTime = StartTime,
                    EndTime = EndTime,
                    Reason = Reason,
                    Remarks = Remarks,
                    GrantedBy = "Admin",
                    GrantedOn = DateTime.Now,
                    Status = "Approved"
                };

                // Grant the leave (service handles allowance deduction)
                var result = _leaveService.GrantLeave(leaveRequest);

                // Show success message
                string message = $"Leave granted successfully for {SelectedEmployee.FullName}!\n\n";
                message += $"Date: {result.LeaveDate:dd MMM yyyy}\n";
                message += $"Duration: {result.LeaveDurationDisplay}\n";
                message += $"Source: {result.LeaveSource}\n";

                if (result.AllowanceMinutesUsed > 0)
                {
                    message += $"\nAllowance Time Used: {result.AllowanceMinutesUsed / 60.0:F2} hours";
                }

                MessageBox.Show(message, "Leave Granted",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset form
                ResetForm();

                // Refresh data
                UpdateAllowanceInfo();
                LoadLeaveHistory();

                StatusMessage = "Leave granted successfully!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error granting leave: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        public void LoadLeaveHistory()
        {
            if (SelectedEmployee == null) return;

            var leaves = _leaveService.GetEmployeeLeaves(SelectedEmployee.Id, DateTime.Now.Year);
            LeaveHistory = new ObservableCollection<LeaveRequest>(leaves);
        }

        [RelayCommand]
        public void CancelSelectedLeave()
        {
            if (SelectedLeaveRecord == null)
            {
                MessageBox.Show("Please select a leave record to cancel.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to cancel this leave?\n\n" +
                $"Employee: {SelectedLeaveRecord.Employee.FullName}\n" +
                $"Date: {SelectedLeaveRecord.LeaveDate:dd MMM yyyy}\n" +
                $"Type: {SelectedLeaveRecord.LeaveType}\n\n" +
                $"Allowance time will be refunded if applicable.",
                "Confirm Cancellation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool success = _leaveService.CancelLeave(SelectedLeaveRecord.Id);

                    if (success)
                    {
                        MessageBox.Show("Leave cancelled successfully. Allowance time has been refunded.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                        LoadLeaveHistory();
                        UpdateAllowanceInfo();
                        StatusMessage = "Leave cancelled successfully";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error cancelling leave: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ViewLeaveStatistics()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please select an employee.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var stats = _leaveService.GetLeaveStatistics(
                SelectedEmployee.Id,
                DateTime.Now.Month,
                DateTime.Now.Year);

            if (stats == null)
            {
                MessageBox.Show("No leave statistics available for this employee.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string message = $"Leave Statistics for {stats.EmployeeName}\n";
            message += $"Month: {DateTime.Now:MMMM yyyy}\n\n";
            message += $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n";
            message += $"Total Leaves: {stats.TotalLeaves}\n";
            message += $"  • Full Day: {stats.FullDayLeaves}\n";
            message += $"  • Half Day: {stats.HalfDayLeaves}\n";
            message += $"  • Custom: {stats.CustomHoursLeaves:F1} hrs\n\n";
            message += $"Total Hours: {stats.TotalLeaveHours:F1} hrs\n";
            message += $"  • Paid (Allowance): {stats.AllowanceTimeUsedHours:F1} hrs\n";
            message += $"  • Unpaid: {stats.UnpaidLeaveHours:F1} hrs\n\n";

            if (stats.HasUnpaidLeave)
            {
                message += $"💰 Salary Deduction: ₹{stats.SalaryDeduction:N2}\n";
            }
            else
            {
                message += $"✅ No salary deduction (fully covered by allowance time)\n";
            }

            MessageBox.Show(message, "Leave Statistics",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public void ExportLeaveReport()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please select an employee.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"LeaveReport_{SelectedEmployee.FullName}_{DateTime.Now:yyyyMMdd}.csv",
                Filter = "CSV Files (*.csv)|*.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("Date,Leave Type,Hours,Start Time,End Time,Source,Reason,Granted By,Status");

                    foreach (var leave in LeaveHistory)
                    {
                        csv.AppendLine($"{leave.LeaveDate:dd/MM/yyyy}," +
                                     $"{leave.LeaveType}," +
                                     $"{leave.LeaveHours}," +
                                     $"{leave.StartTime}," +
                                     $"{leave.EndTime}," +
                                     $"{leave.LeaveSource}," +
                                     $"\"{leave.Reason}\"," +
                                     $"{leave.GrantedBy}," +
                                     $"{leave.Status}");
                    }

                    System.IO.File.WriteAllText(saveDialog.FileName, csv.ToString());
                    MessageBox.Show("Export successful!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }

        private void ResetForm()
        {
            LeaveDate = DateTime.Today;
            SelectedLeaveType = "Half Day";
            StartTime = "09:00";
            EndTime = "13:00";
            CustomHours = 4;
            Reason = string.Empty;
            Remarks = string.Empty;
        }
    }
}