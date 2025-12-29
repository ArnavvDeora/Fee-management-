using System.Collections.Generic;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IFeeCollectionService
    {
        List<Student> SearchStudents(string query);
        List<StudentFee> GetStudentFees(int studentId);
        List<Student> GetStudentsByClass(string standard, string section);
        void ProcessPayment(int studentFeeId, decimal amount, string mode);
    }
}