using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.ObjectModel;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class SalarySettingsViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        [ObservableProperty] private ObservableCollection<SalaryComponent> _components;
        [ObservableProperty] private string _newName;
        [ObservableProperty] private string _newType = "Earning"; // Default
        [ObservableProperty] private string _newCalcType = "Fixed"; // Default
        [ObservableProperty] private decimal _newValue;

        public SalarySettingsViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            LoadComponents();
        }

        [RelayCommand]
        public void LoadComponents()
        {
            var list = _payrollService.GetSalaryComponents();
            Components = new ObservableCollection<SalaryComponent>(list);
        }

        [RelayCommand]
        public void AddComponent()
        {
            if (string.IsNullOrWhiteSpace(NewName)) return;

            var comp = new SalaryComponent
            {
                Name = NewName,
                Type = NewType, // "Earning" or "Deduction"
                CalculationType = NewCalcType, // "Fixed" or "Percentage"
                Value = NewValue,
                IsActive = true
            };

            _payrollService.SaveSalaryComponent(comp);

            // Reset UI
            NewName = ""; NewValue = 0;
            LoadComponents();
        }

        [RelayCommand]
        public void DeleteComponent(SalaryComponent comp)
        {
            if (comp == null) return;
            _payrollService.DeleteSalaryComponent(comp.Id);
            Components.Remove(comp);
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