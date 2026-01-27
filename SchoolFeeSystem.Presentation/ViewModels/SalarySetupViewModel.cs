using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class SalarySetupViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private ObservableCollection<Employee> _employeeList;
        [ObservableProperty] private int _currentPage = 1;
        [ObservableProperty] private int _totalPages = 1;
        [ObservableProperty] private int _pageSize = 10;
        [ObservableProperty] private string _paginationDisplay;
        [ObservableProperty] private string _searchQuery;

        [ObservableProperty] private Employee _selectedEmployee;
        [ObservableProperty] private bool _isEmployeeSelected;
        [ObservableProperty] private decimal _baseSalary;
        [ObservableProperty] private string _payGrade;
        [ObservableProperty] private ObservableCollection<Allowance> _allowancesList = new();
        [ObservableProperty] private ObservableCollection<Deduction> _deductionsList = new();
        [ObservableProperty] private ObservableCollection<SalaryRevision> _historyList = new();

        [ObservableProperty] private decimal _totalAllowances;
        [ObservableProperty] private decimal _totalDeductions;
        [ObservableProperty] private decimal _netPay;

        [ObservableProperty] private string _newAllowanceName;
        [ObservableProperty] private decimal _newAllowanceAmount;
        [ObservableProperty] private string _newDeductionName;
        [ObservableProperty] private decimal _newDeductionAmount;

        public SalarySetupViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadEmployees();
        }

        public void LoadEmployees()
        {
            int totalCount = _payrollService.GetTotalEmployeeCount();
            TotalPages = (int)Math.Ceiling((double)totalCount / PageSize);
            if (TotalPages == 0) TotalPages = 1;
            PaginationDisplay = $"Page {CurrentPage} of {TotalPages}";
            var data = _payrollService.GetEmployeesPaged(CurrentPage, PageSize);
            EmployeeList = new ObservableCollection<Employee>(data);
        }

        [RelayCommand]
        public void NextPage()
        {
            if (CurrentPage < TotalPages) { CurrentPage++; LoadEmployees(); }
        }

        [RelayCommand]
        public void PreviousPage()
        {
            if (CurrentPage > 1) { CurrentPage--; LoadEmployees(); }
        }

        [RelayCommand]
        public void SearchStaff()
        {
            // 1. If search is empty, reload the default list
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                LoadEmployees();
                return;
            }

            // 2. Perform a "Contains" search (Case Insensitive)
            // We search across First Name, Last Name, or Department
            var allEmployees = _payrollService.GetAllEmployees(); // Get everyone first

            var filtered = allEmployees.Where(e =>
                (e.FirstName != null && e.FirstName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.LastName != null && e.LastName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.FullName != null && e.FullName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.Department != null && e.Department.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            // 3. Update the UI List
            EmployeeList = new ObservableCollection<Employee>(filtered);

            // 4. Update display text
            PaginationDisplay = $"Found {filtered.Count} result(s)";
        }

        [RelayCommand]
        public void SelectEmployee(Employee emp)
        {
            if (emp == null) return;
            var fullEmp = _payrollService.GetEmployeeWithSalaryDetails(emp.Id);
            SelectedEmployee = fullEmp;
            BaseSalary = fullEmp.BaseSalary;
            PayGrade = fullEmp.PayGrade;
            AllowancesList = new ObservableCollection<Allowance>(fullEmp.Allowances);
            DeductionsList = new ObservableCollection<Deduction>(fullEmp.Deductions);

            if (fullEmp.SalaryHistory != null)
                HistoryList = new ObservableCollection<SalaryRevision>(fullEmp.SalaryHistory.OrderByDescending(h => h.RevisionDate));

            IsEmployeeSelected = true;
            Recalculate();
        }

        partial void OnBaseSalaryChanged(decimal value) => Recalculate();

        private void Recalculate()
        {
            TotalAllowances = AllowancesList.Sum(a => a.Amount);
            TotalDeductions = DeductionsList.Sum(d => d.Amount);
            NetPay = BaseSalary + TotalAllowances - TotalDeductions;
        }

        [RelayCommand]
        public void AddAllowance()
        {
            if (!string.IsNullOrWhiteSpace(NewAllowanceName) && NewAllowanceAmount > 0)
            {
                AllowancesList.Add(new Allowance { Name = NewAllowanceName, Amount = NewAllowanceAmount, EmployeeId = SelectedEmployee.Id });
                NewAllowanceName = ""; NewAllowanceAmount = 0;
                Recalculate();
            }
        }

        [RelayCommand]
        public void RemoveAllowance(Allowance item)
        {
            AllowancesList.Remove(item);
            Recalculate();
        }

        [RelayCommand]
        public void AddDeduction()
        {
            if (!string.IsNullOrWhiteSpace(NewDeductionName) && NewDeductionAmount > 0)
            {
                DeductionsList.Add(new Deduction { Name = NewDeductionName, Amount = NewDeductionAmount, EmployeeId = SelectedEmployee.Id });
                NewDeductionName = ""; NewDeductionAmount = 0;
                Recalculate();
            }
        }

        [RelayCommand]
        public void RemoveDeduction(Deduction item)
        {
            DeductionsList.Remove(item);
            Recalculate();
        }

        [RelayCommand]
        public void ApplyGlobalRules()
        {
            if (BaseSalary <= 0) { MessageBox.Show("Please enter a Base Salary first."); return; }

            var components = _payrollService.GetSalaryComponents();
            AllowancesList.Clear();
            DeductionsList.Clear();

            foreach (var comp in components)
            {
                decimal amount = comp.CalculationType == "Fixed" ? comp.Value : (BaseSalary * comp.Value) / 100;

                if (comp.Type == "Earning")
                    AllowancesList.Add(new Allowance { Name = comp.Name, Amount = amount, EmployeeId = SelectedEmployee.Id });
                else
                    DeductionsList.Add(new Deduction { Name = comp.Name, Amount = amount, EmployeeId = SelectedEmployee.Id });
            }
            Recalculate();
            MessageBox.Show("Salary structure generated based on global rules!");
        }

        [RelayCommand]
        public void SaveChanges()
        {
            if (SelectedEmployee == null) return;
            SelectedEmployee.BaseSalary = BaseSalary;
            SelectedEmployee.PayGrade = PayGrade;
            SelectedEmployee.Allowances = AllowancesList.ToList();
            SelectedEmployee.Deductions = DeductionsList.ToList();
            _payrollService.SaveSalaryConfiguration(SelectedEmployee, "Updated Salary Structure");
            MessageBox.Show("Saved Successfully!");
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