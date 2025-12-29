using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class FeeStructure
    {
        [Key]
        public int Id { get; set; }

        // Links fee to a specific class (e.g. 10th A)
        [Required]
        public int ClassId { get; set; }

        [ForeignKey("ClassId")]
        public virtual Class Class { get; set; }

        [Required]
        public string FeeName { get; set; } = string.Empty; // e.g., "Tuition Fee", "Lab Charge"

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public DateTime DueDate { get; set; } = DateTime.Now.AddMonths(1);

        // Helper for UI display
        public string DisplayName => $"{FeeName} ({Amount:C})";
    }
}