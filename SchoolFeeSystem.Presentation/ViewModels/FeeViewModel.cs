using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class FeeViewModel : ObservableObject
    {
        private readonly IFeeService _feeService;
        private readonly IStudentService _studentService; // To get classes

        [ObservableProperty]
        private ObservableCollection<Class> _classes;

        [ObservableProperty]
        private Class? _selectedClass;

        [ObservableProperty]
        private string _feeName = string.Empty;

        [ObservableProperty]
        private decimal _amount;

        [ObservableProperty]
        private System.DateTime _dueDate = System.DateTime.Today.AddMonths(1);

        [ObservableProperty]
        private ObservableCollection<FeeStructure> _classFees = new();

        public FeeViewModel(IFeeService feeService, IStudentService studentService)
        {
            _feeService = feeService;
            _studentService = studentService;
            LoadData();
        }

        private void LoadData()
        {
            Classes = new ObservableCollection<Class>(_studentService.GetAllClasses());
        }

        // When user picks a class, load THAT class's fees
        partial void OnSelectedClassChanged(Class? value)
        {
            if (value != null)
            {
                LoadFeesForClass(value.Id);
            }
        }

        private void LoadFeesForClass(int classId)
        {
            var fees = _feeService.GetFeesByClass(classId);
            ClassFees = new ObservableCollection<FeeStructure>(fees);
        }

        [RelayCommand]
        public void SaveFee()
        {
            if (SelectedClass == null)
            {
                MessageBox.Show("Please select a class first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(FeeName) || Amount <= 0)
            {
                MessageBox.Show("Enter valid Fee Name and Amount.");
                return;
            }

            // 1. Prevent Duplicates
            if (ClassFees.Any(f => f.FeeName.ToLower() == FeeName.ToLower()))
            {
                MessageBox.Show($"'{FeeName}' already exists for this class!");
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

            // Refresh List & Clear Inputs
            LoadFeesForClass(SelectedClass.Id);
            FeeName = string.Empty;
            Amount = 0;
        }

        // --- NEW: DELETE BUTTON LOGIC ---
        [RelayCommand]
        public void DeleteFee(FeeStructure fee)
        {
            if (fee == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete '{fee.FeeName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Note: In a real app, you'd check if students have already paid this fee before deleting.
                    // For now, we assume you want to clean up mistakes.
                    _feeService.DeleteFeeStructure(fee.Id);
                    LoadFeesForClass(SelectedClass!.Id);
                }
                catch
                {
                    MessageBox.Show("Could not delete. (You might need to add a Delete method to your Service if missing, or database is locked).");
                }
            }
        }
    }
}