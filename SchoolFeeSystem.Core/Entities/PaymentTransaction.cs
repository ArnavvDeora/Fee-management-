using System;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Entity to track all fee payment transactions
    /// </summary>
    public class PaymentTransaction
    {
        public int Id { get; set; }

        public string StudentName { get; set; }

        public string SheetName { get; set; }

        public string CourseName { get; set; }

        public string Period { get; set; }

        public decimal AmountPaid { get; set; }

        public string PaymentMode { get; set; }

        public DateTime PaymentDate { get; set; }

        public string ProcessedBy { get; set; } // Admin username

        public decimal PreviousBalance { get; set; }

        public decimal NewBalance { get; set; }

        public string PhoneNumber { get; set; }

        public string Remarks { get; set; }

        public string TransactionId { get; set; } // Unique transaction identifier
    }
}