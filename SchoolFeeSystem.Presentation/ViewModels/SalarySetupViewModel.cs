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

        [ObservableProperty]
        private Employee _selectedEmployee;

        [ObservableProperty]
        private bool _isEmployeeSelected;

        [ObservableProperty]
        private decimal _baseSalary;

        [ObservableProperty]
        private ObservableCollection<Allowance> _allowancesList = new();

        [ObservableProperty]
        private ObservableCollection<Deduction> _deductionsList = new();

        [ObservableProperty]
        private decimal _totalAllowances;

        [ObservableProperty]
        private decimal _totalDeductions;

        [ObservableProperty]
        private string _newAllowanceName;

        [ObservableProperty]
        private decimal _newAllowanceAmount;

        [ObservableProperty]
        private string _newDeductionName;

        [ObservableProperty]
        private decimal _newDeductionAmount;

        public SalarySetupViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            System.Diagnostics.Debug.WriteLine("▶▶▶ SalarySetupViewModel CONSTRUCTOR called");
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

            System.Diagnostics.Debug.WriteLine($"▶ LoadEmployees: Loaded {EmployeeList.Count} employees");
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

        [RelayCommand]
        public void SelectEmployee(Employee emp)
        {
            System.Diagnostics.Debug.WriteLine("╔════════════════════════════════════════╗");
            System.Diagnostics.Debug.WriteLine("║   SELECT EMPLOYEE METHOD CALLED!       ║");
            System.Diagnostics.Debug.WriteLine("╚════════════════════════════════════════╝");
            System.Diagnostics.Debug.WriteLine($"▶ Employee parameter: {emp?.FullName ?? "NULL"}");

            if (emp == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ Employee is NULL - RETURNING");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"▶ Step 1: Calling GetEmployeeWithSalaryDetails for ID {emp.Id}");
                var fullEmp = _payrollService.GetEmployeeWithSalaryDetails(emp.Id);
                System.Diagnostics.Debug.WriteLine($"▶ Step 2: fullEmp loaded = {fullEmp != null}");

                if (fullEmp == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ fullEmp is NULL!");
                    MessageBox.Show("Error loading employee details.", "Error");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"▶ Step 3: Setting SelectedEmployee");
                SelectedEmployee = fullEmp;
                System.Diagnostics.Debug.WriteLine($"   SelectedEmployee is now: {SelectedEmployee?.FullName}");

                System.Diagnostics.Debug.WriteLine($"▶ Step 4: Setting BaseSalary = {fullEmp.BaseSalary}");
                BaseSalary = fullEmp.BaseSalary;
                System.Diagnostics.Debug.WriteLine($"   BaseSalary is now: {BaseSalary}");

                System.Diagnostics.Debug.WriteLine($"▶ Step 5: Loading Allowances (count: {fullEmp.Allowances?.Count ?? 0})");
                AllowancesList = new ObservableCollection<Allowance>(
                    fullEmp.Allowances ?? new System.Collections.Generic.List<Allowance>());
                System.Diagnostics.Debug.WriteLine($"   AllowancesList count: {AllowancesList.Count}");

                System.Diagnostics.Debug.WriteLine($"▶ Step 6: Loading Deductions (count: {fullEmp.Deductions?.Count ?? 0})");
                DeductionsList = new ObservableCollection<Deduction>(
                    fullEmp.Deductions ?? new System.Collections.Generic.List<Deduction>());
                System.Diagnostics.Debug.WriteLine($"   DeductionsList count: {DeductionsList.Count}");

                System.Diagnostics.Debug.WriteLine($"▶ Step 7: Setting IsEmployeeSelected = TRUE");
                IsEmployeeSelected = true;
                System.Diagnostics.Debug.WriteLine($"   IsEmployeeSelected is now: {IsEmployeeSelected}");

                System.Diagnostics.Debug.WriteLine($"▶ Step 8: Calling Recalculate()");
                Recalculate();
                System.Diagnostics.Debug.WriteLine($"   TotalAllowances: {TotalAllowances}");
                System.Diagnostics.Debug.WriteLine($"   TotalDeductions: {TotalDeductions}");

                System.Diagnostics.Debug.WriteLine("✅ SELECT EMPLOYEE COMPLETED SUCCESSFULLY!");
                System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌❌❌ EXCEPTION in SelectEmployee: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void AddAllowance()
        {
            System.Diagnostics.Debug.WriteLine($"▶ AddAllowance called - Name: '{NewAllowanceName}', Amount: {NewAllowanceAmount}");

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

            NewAllowanceName = "";
            NewAllowanceAmount = 0;
            Recalculate();

            System.Diagnostics.Debug.WriteLine($"✅ Allowance added. Total: {AllowancesList.Count}");
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

        [RelayCommand]
        public void AddDeduction()
        {
            System.Diagnostics.Debug.WriteLine($"▶ AddDeduction called - Name: '{NewDeductionName}', Amount: {NewDeductionAmount}");

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

            NewDeductionName = "";
            NewDeductionAmount = 0;
            Recalculate();

            System.Diagnostics.Debug.WriteLine($"✅ Deduction added. Total: {DeductionsList.Count}");
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

        private void Recalculate()
        {
            TotalAllowances = AllowancesList.Sum(a => a.Amount);
            TotalDeductions = DeductionsList.Sum(d => d.Amount);
        }

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
                SelectedEmployee.Allowances = AllowancesList.ToList();
                SelectedEmployee.Deductions = DeductionsList.ToList();

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

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var dashboard = services.GetRequiredService<PayrollDashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}