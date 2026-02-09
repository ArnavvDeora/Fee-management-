using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class Transaction
    {
        [Key]
        public int Id { get; set; }

        public int StudentFeeId { get; set; }
        [ForeignKey("StudentFeeId")]
        public virtual StudentFee? StudentFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        public string PaymentMode { get; set; } = "Cash"; // Cash, UPI, Cheque
    }
}