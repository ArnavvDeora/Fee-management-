using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class Class
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // e.g. "10th"

        [Required]
        public string Section { get; set; } = string.Empty; // e.g. "A"

        // Navigation property
        public virtual ICollection<Student> Students { get; set; } = new List<Student>();

        public string DisplayName => $"{Name} - {Section}";
    }
}