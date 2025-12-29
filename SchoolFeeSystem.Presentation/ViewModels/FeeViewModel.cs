using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class FeeViewModel : ObservableObject
    {
        private readonly IFeeService _feeService;

        [ObservableProperty]
        private ObservableCollection<Class> _classes;

        [ObservableProperty]
        private Class? _selectedClass;

        [ObservableProperty]
        private ObservableCollection<FeeStructure> _classFees;

        [ObservableProperty]
        private string _feeName = string.Empty;

        [ObservableProperty]
        private decimal _amount;

        [ObservableProperty]
        private DateTime _dueDate = DateTime.Now.AddMonths(1);

        public FeeViewModel(IFeeService feeService)
        {
            _feeService = feeService;
            LoadClasses();
        }

        private void LoadClasses()
        {
            Classes = new ObservableCollection<Class>(_feeService.GetAllClasses());
        }

        // When user selects a class in dropdown, load that class's fees
        partial void OnSelectedClassChanged(Class? value)
        {
            if (value != null)
            {
                LoadFees(value.Id);
            }
        }

        private void LoadFees(int classId)
        {
            ClassFees = new ObservableCollection<FeeStructure>(_feeService.GetFeesByClass(classId));
        }

        [RelayCommand]
        public void SaveFee()
        {
            if (SelectedClass == null)
            {
                MessageBox.Show("Select a Class first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(FeeName) || Amount <= 0)
            {
                MessageBox.Show("Enter valid Fee Name and Amount.");
                return;
            }

            var newFee = new FeeStructure
            {
                ClassId = SelectedClass.Id,
                FeeName = FeeName,
                Amount = Amount,
                DueDate = DueDate
            };

            _feeService.AddFeeStructure(newFee);

            MessageBox.Show("Fee Structure Saved!");
            LoadFees(SelectedClass.Id); // Refresh list

            // Clear inputs
            FeeName = string.Empty;
            Amount = 0;
        }
    }
}