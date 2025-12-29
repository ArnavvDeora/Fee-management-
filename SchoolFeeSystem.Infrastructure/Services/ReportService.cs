using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class ReportService : IReportService
    {
        private readonly AppDbContext _context;

        public ReportService(AppDbContext context)
        {
            _context = context;
        }

        public decimal GetTotalCollectionToday()
        {
            // This avoids the SQLite "decimal sum" crash.
            var todayTransactions = _context.Transactions
                .AsEnumerable() // Forces client-side evaluation
                .Where(t => t.PaymentDate.Date == DateTime.Today)
                .ToList();

            return todayTransactions.Sum(t => t.AmountPaid);
        }

        public decimal GetTotalPendingAmount()
        {
            // Logic: (Fee Amount - Amount Paid) for all unpaid records
            var totalDues = _context.StudentFees
                .Include(sf => sf.FeeStructure)
                .ToList(); // Load into memory to calculate pending safely

            return totalDues.Sum(sf => sf.PendingAmount);
        }

        public int GetTotalStudents()
        {
            return _context.Students.Count(s => s.IsActive);
        }

        public List<Transaction> GetRecentTransactions()
        {
            // Get last 20 transactions
            return _context.Transactions
                .Include(t => t.StudentFee)
                .ThenInclude(sf => sf.Student)
                .OrderByDescending(t => t.PaymentDate)
                .Take(20)
                .ToList();
        }
    }
}