using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class PayrollReportsViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // Filters
        [ObservableProperty] private ObservableCollection<Employee> _employees;
        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private DateTime _selectedMonth = DateTime.Now;

        // Report Data
        [ObservableProperty] private ObservableCollection<AttendanceReportItem> _attendanceList;
        [ObservableProperty] private ObservableCollection<SalaryReportItem> _salaryList;

        public PayrollReportsViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadFilters();
        }

        private void LoadFilters()
        {
            var list = _payrollService.GetAllEmployees();
            list.Insert(0, new Employee { Id = 0, FirstName = "All", LastName = "Employees" });
            Employees = new ObservableCollection<Employee>(list);
            SelectedEmployee = Employees[0];
        }

        [RelayCommand]
        public void GenerateReports()
        {
            int? empId = (SelectedEmployee?.Id > 0) ? SelectedEmployee.Id : (int?)null;
            int m = SelectedMonth.Month;
            int y = SelectedMonth.Year;

            // 1. Fetch Attendance Report
            var attData = _payrollService.GetAttendanceReport(m, y, empId);
            AttendanceList = new ObservableCollection<AttendanceReportItem>(attData);

            // 2. Fetch Salary Report
            var salData = _payrollService.GetSalaryReport(m, y, empId);
            SalaryList = new ObservableCollection<SalaryReportItem>(salData);
        }

        [RelayCommand]
        public void ExportAttendanceCsv()
        {
            if (AttendanceList == null || AttendanceList.Count == 0) return;
            SaveCsv("Attendance_Report", "Name,Designation,Present,Absent,Holidays,Total Payable",
                sb => {
                    foreach (var item in AttendanceList)
                        sb.AppendLine($"{item.EmployeeName},{item.Designation},{item.PresentDays},{item.AbsentDays},{item.Holidays},{item.TotalPayable}");
                });
        }

        [RelayCommand]
        public void ExportSalaryCsv()
        {
            if (SalaryList == null || SalaryList.Count == 0) return;
            SaveCsv("Salary_Report", "Name,Designation,Base Salary,Allowances,Deductions,Net Salary",
                sb => {
                    foreach (var item in SalaryList)
                        sb.AppendLine($"{item.EmployeeName},{item.Designation},{item.BaseSalary},{item.TotalAllowances},{item.TotalDeductions},{item.NetSalary}");
                });
        }

        private void SaveCsv(string prefix, string header, Action<StringBuilder> writeRows)
        {
            SaveFileDialog dlg = new SaveFileDialog { FileName = $"{prefix}_{DateTime.Now:yyyyMMdd}.csv", Filter = "CSV File|*.csv" };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine(header);
                writeRows(sb);
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show("Export Successful!");
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            // Since this is the Payroll Report, go back to Payroll Dashboard
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}