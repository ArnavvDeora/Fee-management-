using SchoolFeeSystem.Core.Entities;
using System.Collections.Generic;
using System.Security.Claims;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IStudentService
    {
        List<Student> GetAllStudents();
        List<Class> GetAllClasses();
        void AddStudent(Student student);
        void AddClass(Class newClass);
    }
}