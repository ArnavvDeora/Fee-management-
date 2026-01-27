using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    public class AttendanceSettings
    {
        [Key]
        public int Id { get; set; }

        // Rules
        public TimeSpan StartTime { get; set; } = new TimeSpan(9, 0, 0); // 09:00 AM
        public TimeSpan LateMarkTime { get; set; } = new TimeSpan(9, 15, 0); // 09:15 AM (Late)

        public TimeSpan HalfDayHours { get; set; } = new TimeSpan(4, 0, 0); // < 4 hours = Half Day
        public TimeSpan ShortLeaveHours { get; set; } = new TimeSpan(6, 0, 0); // < 6 hours = Short Leave
    }
}