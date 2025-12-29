using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class StudentService : IStudentService
    {
        private readonly AppDbContext _context;

        public StudentService(AppDbContext context)
        {
            _context = context;
        }

        public List<Student> GetAllStudents()
        {
            // Include Class details when fetching students
            return _context.Students.Include(s => s.Class).ToList();
        }

        public List<Class> GetAllClasses()
        {
            return _context.Classes.ToList();
        }

        public void AddStudent(Student student)
        {
            _context.Students.Add(student);
            _context.SaveChanges();
        }

        public void AddClass(Class newClass)
        {
            _context.Classes.Add(newClass);
            _context.SaveChanges();
        }
    }
}