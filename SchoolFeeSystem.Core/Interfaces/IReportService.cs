using System.Collections.Generic;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IReportService
    {
        decimal GetTotalCollectionToday();
        decimal GetTotalPendingAmount();
        int GetTotalStudents();
        List<Transaction> GetRecentTransactions();
    }
}