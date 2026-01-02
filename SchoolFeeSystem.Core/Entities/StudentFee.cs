using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Transactions;

namespace SchoolFeeSystem.Core.Entities
{
    public class StudentFee
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }
        [ForeignKey("StudentId")]
        public virtual Student Student { get; set; } = null!;

        public int FeeStructureId { get; set; }
        [ForeignKey("FeeStructureId")]
        public virtual FeeStructure FeeStructure { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; } = 0;

        public string Status { get; set; } = "Unpaid"; // Unpaid, Partial, Paid

        // Helper to calculate what is left
        [NotMapped]
        public decimal PendingAmount => (FeeStructure != null) ? FeeStructure.Amount - AmountPaid : 0;

        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}