using SchoolFeeSystem.Core.Entities;
using System.Collections.Generic;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IAttendanceService
    {
        List<AttendanceRecord> GetAttendance(int month, int year, int? employeeId = null);
        void MarkAttendance(AttendanceRecord record);
        void BulkMarkAttendance(List<AttendanceRecord> records);

        // --- HOLIDAYS ---
        void AddHoliday(Holiday holiday);
        void DeleteHoliday(int id);
        List<Holiday> GetHolidays(int year);

        // --- NEW: SMART IMPORT ---
        void ImportBiometricReport(string filePath);
        void ImportAttendance(string fileName);
        IEnumerable<AttendanceRecord> GetRecords(int id, int month, int year);
        void UpdateRecord(AttendanceRecord record);
    }
}