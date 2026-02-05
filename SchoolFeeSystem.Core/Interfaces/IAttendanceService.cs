using SchoolFeeSystem.Core.Entities;
using System; // Required for IProgress
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IAttendanceService
    {
        // --- DATA RETRIEVAL ---
        List<AttendanceRecord> GetAttendance(int month, int year, int? employeeId = null);
        IEnumerable<AttendanceRecord> GetRecords(int id, int month, int year);

        // --- ATTENDANCE MARKING ---
        void MarkAttendance(AttendanceRecord record);
        void AddAttendanceRecord(AttendanceRecord record);
        void BulkMarkAttendance(List<AttendanceRecord> records);
        void UpdateRecord(AttendanceRecord record);
        Task ImportAttendanceAsync(string filePath, IProgress<string> progress = null);

        // --- FILE IMPORTS ---
        void ImportBiometricReport(string filePath);

        // [FIXED] Now accepts an optional progress reporter
        void ImportAttendance(string filePath, IProgress<string> progress = null);

        // --- HOLIDAY MANAGEMENT ---
        void AddHoliday(Holiday holiday);
        void DeleteHoliday(int id);
        List<Holiday> GetHolidays(int year);
    }
}