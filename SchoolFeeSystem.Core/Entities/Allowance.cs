using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolFeeSystem.Core.Entities
{
    public class Allowance
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "House Rent"
        public decimal Amount { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;
    }
}