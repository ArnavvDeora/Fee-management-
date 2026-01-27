using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class Deduction
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "Tax"
        public decimal Amount { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;
    }
}