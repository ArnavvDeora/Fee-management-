using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class SalaryComponent
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty; // e.g., "HRA", "PF"
        public string Type { get; set; } = "Earning"; // "Earning" or "Deduction"

        // "Fixed" (e.g. 5000) or "Percentage" (e.g. 12% of Basic)
        public string CalculationType { get; set; } = "Fixed";

        public decimal Value { get; set; } // The Amount or The Percentage
        public bool IsActive { get; set; } = true;
    }
}