using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class FeeCollectionViewModel : ObservableObject
    {
        private readonly IFeeCollectionService _feeService;

        // --- FILTERING ---
        public List<string> Standards { get; } = new() { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th", "11th", "12th" };
        public List<string> Sections { get; } = new() { "A", "B", "C", "D", "E" };

        [ObservableProperty]
        private string _selectedStandard;

        [ObservableProperty]
        private string _selectedSection;

        // --- SEARCH & LIST ---
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<Student> _searchResults = new();

        [ObservableProperty]
        private Student? _selectedStudent;

        // --- PAYMENT DATA ---
        [ObservableProperty]
        private ObservableCollection<StudentFee> _studentFees = new();

        [ObservableProperty]
        private StudentFee? _selectedFeeToPay;

        [ObservableProperty]
        private decimal _paymentAmount;

        [ObservableProperty]
        private string _paymentMode = "Cash";

        public ObservableCollection<string> PaymentModes { get; } = new() { "Cash", "UPI", "Cheque", "Card" };

        public FeeCollectionViewModel(IFeeCollectionService feeService)
        {
            _feeService = feeService;
        }

        // Triggered when Dropdowns Change
        [RelayCommand]
        public void LoadClassData()
        {
            if (string.IsNullOrEmpty(SelectedStandard) || string.IsNullOrEmpty(SelectedSection)) return;

            var students = _feeService.GetStudentsByClass(SelectedStandard, SelectedSection);

            // Order by Dues (Defaulters at top), then Name
            var sorted = students.OrderByDescending(s => s.TotalDues).ThenBy(s => s.FullName);

            SearchResults = new ObservableCollection<Student>(sorted);
        }

        [RelayCommand]
        public void Search()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;
            var results = _feeService.SearchStudents(SearchText);
            SearchResults = new ObservableCollection<Student>(results);
        }

        partial void OnSelectedStudentChanged(Student? value)
        {
            if (value != null)
            {
                var fees = _feeService.GetStudentFees(value.Id);
                StudentFees = new ObservableCollection<StudentFee>(fees);
                SelectedFeeToPay = null;
                PaymentAmount = 0;
            }
        }

        partial void OnSelectedFeeToPayChanged(StudentFee? value)
        {
            if (value != null) PaymentAmount = value.PendingAmount;
        }

        [RelayCommand]
        public void SubmitPayment()
        {
            if (SelectedFeeToPay == null || PaymentAmount <= 0) return;
            if (PaymentAmount > SelectedFeeToPay.PendingAmount)
            {
                MessageBox.Show("Amount exceeds pending balance.");
                return;
            }

            _feeService.ProcessPayment(SelectedFeeToPay.Id, PaymentAmount, PaymentMode);
            MessageBox.Show("Payment Successful!");

            // Refresh Fees
            var fees = _feeService.GetStudentFees(SelectedStudent!.Id);
            StudentFees = new ObservableCollection<StudentFee>(fees);
            SelectedFeeToPay = null;
            PaymentAmount = 0;

            // Refresh the Main List (to update the Red/Green status instantly)
            LoadClassData();
        }

        [RelayCommand]
        public void SendReminder()
        {
            if (SelectedStudent == null) return;
            decimal totalPending = StudentFees.Sum(f => f.PendingAmount);
            if (totalPending <= 0) { MessageBox.Show("No pending dues!"); return; }

            string message = $"Reminder: Your ward {SelectedStudent.FullName} has pending fees of {totalPending:C}. Please pay immediately.";
            string phoneNumber = SelectedStudent.ContactNumber.Replace(" ", "").Replace("-", "");
            if (!phoneNumber.StartsWith("91")) phoneNumber = "91" + phoneNumber;

            try { Process.Start(new ProcessStartInfo { FileName = $"https://wa.me/{phoneNumber}?text={System.Uri.EscapeDataString(message)}", UseShellExecute = true }); }
            catch { MessageBox.Show("Could not open WhatsApp."); }
        }
    }
}