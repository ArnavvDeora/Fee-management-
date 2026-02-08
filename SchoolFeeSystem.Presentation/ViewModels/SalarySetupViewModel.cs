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
    /// <summary>
    /// ViewModel for managing monthly incentives and deductions
    /// Admin can add/remove incentives and deductions that affect monthly payroll
    /// </summary>
    public partial class SalarySetupViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // ========================================
        // EMPLOYEE LIST & PAGINATION
        // ========================================
        [ObservableProperty]
        private ObservableCollection<Employee> _employeeList;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private int _pageSize = 10;

        [ObservableProperty]
        private string _paginationDisplay;

        [ObservableProperty]
        private string _searchQuery;

        // ========================================
        // SELECTED EMPLOYEE
        // ========================================
        [ObservableProperty]
        private Employee _selectedEmployee;

        [ObservableProperty]
        private bool _isEmployeeSelected;

        /// <summary>
        /// Basic Salary (READ-ONLY - for display purposes only)
        /// </summary>
        [ObservableProperty]
        private decimal _baseSalary;

        // ========================================
        // INCENTIVES & DEDUCTIONS
        // ========================================
        [ObservableProperty]
        private ObservableCollection<Allowance> _allowancesList = new();

        [ObservableProperty]
        private ObservableCollection<Deduction> _deductionsList = new();

        [ObservableProperty]
        private decimal _totalAllowances;

        [ObservableProperty]
        private decimal _totalDeductions;

        /// <summary>
        /// New incentive entry fields
        /// </summary>
        [ObservableProperty]
        private string _newAllowanceName;

        [ObservableProperty]
        private decimal _newAllowanceAmount;

        /// <summary>
        /// New deduction entry fields
        /// </summary>
        [ObservableProperty]
        private string _newDeductionName;

        [ObservableProperty]
        private decimal _newDeductionAmount;

        public SalarySetupViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadEmployees();
        }

        // =========================================================
        // EMPLOYEE LOADING & SEARCH
        // =========================================================

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
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                LoadEmployees();
            }
        }

        [RelayCommand]
        public void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                LoadEmployees();
            }
        }

        [RelayCommand]
        public void SearchStaff()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                LoadEmployees();
                return;
            }

            var allEmployees = _payrollService.GetAllEmployees();

            var filtered = allEmployees.Where(e =>
                (e.FirstName != null && e.FirstName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.LastName != null && e.LastName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.FullName != null && e.FullName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.Department != null && e.Department.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                (e.BiometricId != null && e.BiometricId.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            EmployeeList = new ObservableCollection<Employee>(filtered);
            PaginationDisplay = $"Found {filtered.Count} result(s)";
        }

        // =========================================================
        // EMPLOYEE SELECTION
        // =========================================================

        [RelayCommand]
        public void SelectEmployee(Employee emp)
        {
            if (emp == null) return;

            // Load full employee details with allowances and deductions
            var fullEmp = _payrollService.GetEmployeeWithSalaryDetails(emp.Id);
            SelectedEmployee = fullEmp;

            // Set basic salary (READ-ONLY display)
            BaseSalary = fullEmp.BaseSalary;

            // Load existing incentives and deductions
            AllowancesList = new ObservableCollection<Allowance>(
                fullEmp.Allowances ?? new System.Collections.Generic.List<Allowance>());

            DeductionsList = new ObservableCollection<Deduction>(
                fullEmp.Deductions ?? new System.Collections.Generic.List<Deduction>());

            // Show the details panel
            IsEmployeeSelected = true;

            // Calculate totals
            Recalculate();
        }

        // =========================================================
        // INCENTIVES MANAGEMENT
        // =========================================================

        [RelayCommand]
        public void AddAllowance()
        {
            if (string.IsNullOrWhiteSpace(NewAllowanceName))
            {
                MessageBox.Show("Please enter an incentive name.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NewAllowanceAmount <= 0)
            {
                MessageBox.Show("Please enter a valid amount.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AllowancesList.Add(new Allowance
            {
                Name = NewAllowanceName,
                Amount = NewAllowanceAmount,
                EmployeeId = SelectedEmployee.Id
            });

            // Clear fields
            NewAllowanceName = "";
            NewAllowanceAmount = 0;

            Recalculate();
        }

        [RelayCommand]
        public void RemoveAllowance(Allowance item)
        {
            if (item == null) return;

            var result = MessageBox.Show(
                $"Remove incentive '{item.Name}' (₹{item.Amount:N2})?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                AllowancesList.Remove(item);
                Recalculate();
            }
        }

        // =========================================================
        // DEDUCTIONS MANAGEMENT
        // =========================================================

        [RelayCommand]
        public void AddDeduction()
        {
            if (string.IsNullOrWhiteSpace(NewDeductionName))
            {
                MessageBox.Show("Please enter a deduction name.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NewDeductionAmount <= 0)
            {
                MessageBox.Show("Please enter a valid amount.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DeductionsList.Add(new Deduction
            {
                Name = NewDeductionName,
                Amount = NewDeductionAmount,
                EmployeeId = SelectedEmployee.Id
            });

            // Clear fields
            NewDeductionName = "";
            NewDeductionAmount = 0;

            Recalculate();
        }

        [RelayCommand]
        public void RemoveDeduction(Deduction item)
        {
            if (item == null) return;

            var result = MessageBox.Show(
                $"Remove deduction '{item.Name}' (₹{item.Amount:N2})?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DeductionsList.Remove(item);
                Recalculate();
            }
        }

        // =========================================================
        // CALCULATION
        // =========================================================

        private void Recalculate()
        {
            TotalAllowances = AllowancesList.Sum(a => a.Amount);
            TotalDeductions = DeductionsList.Sum(d => d.Amount);
        }

        // =========================================================
        // SAVE CHANGES
        // =========================================================

        [RelayCommand]
        public void SaveChanges()
        {
            if (SelectedEmployee == null)
            {
                MessageBox.Show("No employee selected.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Update employee's allowances and deductions
                SelectedEmployee.Allowances = AllowancesList.ToList();
                SelectedEmployee.Deductions = DeductionsList.ToList();

                // Save to database
                _payrollService.SaveSalaryConfiguration(
                    SelectedEmployee,
                    "Updated monthly incentives and deductions");

                MessageBox.Show(
                    $"Incentives & deductions saved successfully for {SelectedEmployee.FullName}!\n\n" +
                    $"Total Incentives: ₹{TotalAllowances:N2}\n" +
                    $"Total Deductions: ₹{TotalDeductions:N2}\n\n" +
                    "These will be reflected in the next payroll calculation.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving changes: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =========================================================
        // NAVIGATION
        // =========================================================

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}