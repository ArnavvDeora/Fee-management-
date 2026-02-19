using System.Collections.Generic;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    // FIX: Removed GenerateFeesForStudent and GenerateFeesForClass.
    // These were incorrectly added to this interface in a previous suggestion.
    // Your FeeCollectionViewModel uses CsvDataService directly, not this interface,
    // so those methods don't belong here and caused the CS0535 build errors.

    public interface IFeeCollectionService
    {
        List<Student> SearchStudents(string query);
        List<StudentFee> GetStudentFees(int studentId);
        List<Student> GetStudentsByClass(string standard, string section);
        void ProcessPayment(int studentFeeId, decimal amount, string mode);
    }
}