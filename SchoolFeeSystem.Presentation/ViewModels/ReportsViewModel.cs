using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.ObjectModel;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly IReportService _reportService;

        [ObservableProperty]
        private decimal _todayCollection;

        [ObservableProperty]
        private decimal _totalPending;

        [ObservableProperty]
        private int _studentCount;

        [ObservableProperty]
        private ObservableCollection<Transaction> _recentTransactions;

        public ReportsViewModel(IReportService reportService)
        {
            _reportService = reportService;
            LoadData();
        }

        [RelayCommand]
        public void Refresh()
        {
            LoadData();
        }

        private void LoadData()
        {
            TodayCollection = _reportService.GetTotalCollectionToday();
            TotalPending = _reportService.GetTotalPendingAmount();
            StudentCount = _reportService.GetTotalStudents();
            RecentTransactions = new ObservableCollection<Transaction>(_reportService.GetRecentTransactions());
        }
    }
}