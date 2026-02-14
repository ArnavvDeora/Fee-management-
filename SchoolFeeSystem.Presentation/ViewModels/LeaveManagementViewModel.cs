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
        private readonly ICompanyGatePassService _gatePassService;

        // ✅ NEW: Search properties
        private List<Employee> _allEmployees = new List<Employee>();

        [ObservableProperty]
        private string _searchText = string.Empty;

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

        // Company Gate Pass Properties
        [ObservableProperty]
        private int _gatePassRemainingMinutes = 120;

        [ObservableProperty]
        private int _gatePassRemainingUses = 2;

        [ObservableProperty]
        private bool _hasGatePassAvailable = true;

        [ObservableProperty]
        private string _gatePassStatus = "2h 0m (2 uses left)";

        [ObservableProperty]
        private string _gatePassWarning = "";

        public List<string> LeaveTypes { get; } = new List<string>
        {
            "Full Day",
            "Half Day",
            "Custom Hours"
        };

        public LeaveManagementViewModel(
            ILeaveService leaveService,
            IPayrollService payrollService,
            ICompanyGatePassService gatePassService)
        {
            _leaveService = leaveService;
            _payrollService = payrollService;
            _gatePassService = gatePassService;

            LoadEmployees();
        }

        private void LoadEmployees()
        {
            var list = _payrollService.GetAllEmployees();
            _allEmployees = list;
            Employees = new ObservableCollection<Employee>(list);

            if (Employees.Count > 0)
            {
                SelectedEmployee = Employees[0];
            }
        }

        // ✅ NEW: Search command
        [RelayCommand]
        public void SearchEmployee()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Show all employees if search is empty
                Employees = new ObservableCollection<Employee>(_allEmployees);
            }
            else
            {
                // Filter employees by name or biometric ID
                var filtered = _allEmployees.Where(e =>
                    (e.FullName != null && e.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.BiometricId != null && e.BiometricId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.FirstName != null && e.FirstName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (e.LastName != null && e.LastName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                Employees = new ObservableCollection<Employee>(filtered);
            }

            // Auto-select first result if available
            if (Employees.Count > 0)
            {
                SelectedEmployee = Employees[0];
                StatusMessage = $"Found {Employees.Count} employee(s)";
            }
            else
            {
                SelectedEmployee = null;
                StatusMessage = "No employees found";
            }
        }

        // ✅ NEW: Clear search
        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
            SearchEmployee();
            StatusMessage = "Search cleared";
        }

        partial void OnSelectedEmployeeChanged(Employee value)
        {
            if (value != null)
            {
                UpdateAllowanceInfo();
                UpdateGatePassInfo();
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

        [RelayCommand]
        public void UpdateGatePassInfo()
        {
            if (SelectedEmployee == null) return;

            var stats = _gatePassService.GetGatePassStatistics(
                SelectedEmployee.Id,
                DateTime.Now.Month,
                DateTime.Now.Year
            );

            GatePassRemainingMinutes = stats.RemainingMinutes;
            GatePassRemainingUses = stats.RemainingUses;
            HasGatePassAvailable = stats.RemainingUses > 0 && stats.RemainingMinutes > 0;

            // Format status display
            int hours = stats.RemainingMinutes / 60;
            int mins = stats.RemainingMinutes % 60;
            GatePassStatus = $"{hours}h {mins}m ({stats.RemainingUses} uses left)";

            // Update warning message
            if (stats.IsExhausted)
            {
                GatePassWarning = "⚠️ Company Gate Pass exhausted for this month";
            }
            else if (stats.RemainingUses == 0)
            {
                GatePassWarning = "⚠️ Max uses reached (2/month limit)";
            }
            else if (stats.RemainingMinutes == 0)
            {
                GatePassWarning = "⚠️ All time used from Company Gate Pass";
            }
            else
            {
                GatePassWarning = "";
            }

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

            int leaveMinutes = (int)(leaveHours * 60);

            int gatePassMinutes = 0;
            int personalAllowanceMinutes = 0;
            int unpaidMinutes = leaveMinutes;

            // Priority 1: Company Gate Pass
            if (HasGatePassAvailable && GatePassRemainingMinutes > 0)
            {
                gatePassMinutes = Math.Min(leaveMinutes, GatePassRemainingMinutes);
                unpaidMinutes -= gatePassMinutes;
            }

            // Priority 2: Personal Allowance
            if (unpaidMinutes > 0 && HasAllowanceTime)
            {
                int availableAllowanceMinutes = (int)(AvailableAllowanceHours * 60);
                personalAllowanceMinutes = Math.Min(unpaidMinutes, availableAllowanceMinutes);
                unpaidMinutes -= personalAllowanceMinutes;
            }

            // Build preview message
            var preview = new System.Text.StringBuilder();

            if (gatePassMinutes > 0)
            {
                decimal gatePassHours = gatePassMinutes / 60m;
                preview.Append($"Company Pass: {gatePassHours:F1}h");
            }

            if (personalAllowanceMinutes > 0)
            {
                decimal personalHours = personalAllowanceMinutes / 60m;
                if (preview.Length > 0) preview.Append(" + ");
                preview.Append($"Personal: {personalHours:F1}h");
            }

            if (unpaidMinutes > 0)
            {
                decimal unpaidHours = unpaidMinutes / 60m;
                if (preview.Length > 0) preview.Append(" + ");
                preview.Append($"Unpaid: {unpaidHours:F1}h");
            }

            LeaveSourcePreview = preview.ToString();
        }

        [RelayCommand]
        public void CalculateCustomHours()
        {
            if (string.IsNullOrEmpty(StartTime) || string.IsNullOrEmpty(EndTime))
            {
                MessageBox.Show("Please enter valid start and end times.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var start = TimeSpan.Parse(StartTime);
                var end = TimeSpan.Parse(EndTime);

                if (end <= start)
                {
                    MessageBox.Show("End time must be after start time.", "Invalid Time Range",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var duration = end - start;
                CustomHours = (decimal)duration.TotalHours;
                UpdateLeaveSourcePreview();
            }
            catch
            {
                MessageBox.Show("Invalid time format. Use HH:mm format (e.g., 09:00).", "Invalid Format",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void GrantLeave()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please select an employee.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(Reason))
            {
                MessageBox.Show("Please provide a reason for the leave.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal leaveHours = SelectedLeaveType == "Full Day" ? 8 :
                               SelectedLeaveType == "Half Day" ? 4 :
                               CustomHours;

            if (leaveHours <= 0 || leaveHours > 24)
            {
                MessageBox.Show("Leave hours must be between 0 and 24.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _leaveService.GrantLeave(new LeaveRequest
                {
                    EmployeeId = SelectedEmployee.Id,
                    LeaveDate = LeaveDate,
                    LeaveType = SelectedLeaveType,
                    LeaveHours = leaveHours,
                    StartTime = SelectedLeaveType == "Custom Hours" ? StartTime : null,
                    EndTime = SelectedLeaveType == "Custom Hours" ? EndTime : null,
                    Reason = Reason,
                    Remarks = Remarks,
                    Status = "Approved",
                    GrantedBy = "Admin",
                    GrantedOn = DateTime.Now
                });

                MessageBox.Show(
                    $"Leave granted successfully!\n\n" +
                    $"Employee: {SelectedEmployee.FullName}\n" +
                    $"Date: {LeaveDate:dd-MM-yyyy}\n" +
                    $"Type: {SelectedLeaveType}\n" +
                    $"Hours: {leaveHours:F1}\n\n" +
                    $"{LeaveSourcePreview}",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                UpdateAllowanceInfo();
                UpdateGatePassInfo();
                LoadLeaveHistory();
                ClearForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error granting leave: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearForm()
        {
            LeaveDate = DateTime.Today;
            SelectedLeaveType = "Half Day";
            Reason = string.Empty;
            Remarks = string.Empty;
            StatusMessage = "Leave granted successfully";
        }

        private void LoadLeaveHistory()
        {
            if (SelectedEmployee == null) return;

            var leaves = _leaveService.GetEmployeeLeaves(
                SelectedEmployee.Id,
                DateTime.Now.Year);

            LeaveHistory = new ObservableCollection<LeaveRequest>(leaves);
        }

        [RelayCommand]
        public void ShowStatistics()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please select an employee.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var stats = _leaveService.GetLeaveStatistics(
                SelectedEmployee.Id,
                DateTime.Now.Month,
                DateTime.Now.Year);

            var gatePassStats = _gatePassService.GetGatePassStatistics(
                SelectedEmployee.Id,
                DateTime.Now.Month,
                DateTime.Now.Year);

            MessageBox.Show(
                $"Leave Statistics for {SelectedEmployee.FullName}\n" +
                $"Month: {DateTime.Now:MMMM yyyy}\n\n" +
                $"--- LEAVE SUMMARY ---\n" +
                $"Total Leaves: {stats.TotalLeaves}\n" +
                $"Full Days: {stats.FullDayLeaves}\n" +
                $"Half Days: {stats.HalfDayLeaves}\n" +
                $"Total Hours: {stats.TotalLeaveHours:F1}\n\n" +
                $"--- COMPANY GATE PASS ---\n" +
                $"Used: {gatePassStats.UsedMinutes / 60m:F1} hours\n" +
                $"Remaining: {gatePassStats.RemainingMinutes / 60m:F1} hours\n" +
                $"Uses: {gatePassStats.TimesUsed}/{gatePassStats.TimesUsed + gatePassStats.RemainingUses}\n" +
                $"Status: {gatePassStats.Status}\n\n" +
                $"--- PERSONAL ALLOWANCE ---\n" +
                $"Available: {AvailableAllowanceHours:F1} hours\n\n" +
                $"--- FINANCIAL IMPACT ---\n" +
                $"Paid Hours: {stats.PaidLeaveHours:F1}\n" +
                $"Unpaid Hours: {stats.UnpaidLeaveHours:F1}\n" +
                $"Salary Deduction: ₹{stats.SalaryDeduction:N2}",
                "Leave Statistics",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void ExportReport()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("Please select an employee.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (LeaveHistory == null || LeaveHistory.Count == 0)
            {
                MessageBox.Show("No leave records to export.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"LeaveReport_{SelectedEmployee.FullName}_{DateTime.Now:yyyyMMdd}.csv",
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = "csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var csv = new System.Text.StringBuilder();

                    csv.AppendLine("Employee Name,Leave Date,Leave Type,Hours,Time,Source,Reason,Status,Granted By,Granted On");

                    foreach (var leave in LeaveHistory)
                    {
                        var timeRange = leave.LeaveType == "Custom Hours"
                            ? $"{leave.StartTime} - {leave.EndTime}"
                            : "-";

                        csv.AppendLine($"\"{SelectedEmployee.FullName}\"," +
                                     $"{leave.LeaveDate:dd-MM-yyyy}," +
                                     $"\"{leave.LeaveType}\"," +
                                     $"{leave.LeaveHours:F1}," +
                                     $"\"{timeRange}\"," +
                                     $"\"{leave.LeaveSource}\"," +
                                     $"\"{leave.Reason}\"," +
                                     $"\"{leave.Status}\"," +
                                     $"\"{leave.GrantedBy}\"," +
                                     $"{leave.GrantedOn:dd-MM-yyyy HH:mm}");
                    }

                    System.IO.File.WriteAllText(saveDialog.FileName, csv.ToString());

                    MessageBox.Show(
                        $"Leave report exported successfully!\n\n" +
                        $"Records: {LeaveHistory.Count}\n" +
                        $"File: {System.IO.Path.GetFileName(saveDialog.FileName)}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    StatusMessage = "Export completed successfully";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Export failed";
            }
        }

        [RelayCommand]
        public void CancelSelectedLeave()
        {
            if (SelectedLeaveRecord == null)
            {
                MessageBox.Show("Please select a leave record to cancel.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to cancel this leave?\n\n" +
                $"Date: {SelectedLeaveRecord.LeaveDate:dd-MM-yyyy}\n" +
                $"Type: {SelectedLeaveRecord.LeaveType}\n" +
                $"Reason: {SelectedLeaveRecord.Reason}",
                "Confirm Cancellation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                bool success = _leaveService.CancelLeave(SelectedLeaveRecord.Id);

                if (success)
                {
                    MessageBox.Show(
                        $"Leave cancelled successfully!\n\n" +
                        $"Refunded:\n" +
                        $"• Allowance time: {SelectedLeaveRecord.AllowanceMinutesUsed / 60m:F1} hours\n" +
                        $"• Gate Pass & Personal Allowance restored\n\n" +
                        $"Note: This leave cannot be cancelled again.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    UpdateAllowanceInfo();
                    UpdateGatePassInfo();
                    LoadLeaveHistory();
                }
                else
                {
                    MessageBox.Show(
                        "Failed to cancel leave.\n\n" +
                        "This leave may already be cancelled or deleted.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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

        [RelayCommand]
        public void RefreshData()
        {
            UpdateAllowanceInfo();
            UpdateGatePassInfo();
            LoadLeaveHistory();
            StatusMessage = "Data refreshed";
        }
    }
}