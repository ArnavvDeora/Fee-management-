using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class Holiday
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // e.g. "Diwali"
        public DateTime Date { get; set; }
        public bool IsRecurring { get; set; } = true; // Every year?
    }
}