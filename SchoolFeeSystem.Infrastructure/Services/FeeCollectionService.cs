using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class FeeCollectionService : IFeeCollectionService
    {
        private readonly AppDbContext _context;

        public FeeCollectionService(AppDbContext context)
        {
            _context = context;
        }

        // --- NEW METHOD: Filter by Class & Auto-Generate Dues ---
        public List<Student> GetStudentsByClass(string standard, string section)
        {
            // 1. Get the class ID
            var classObj = _context.Classes.FirstOrDefault(c => c.Name == standard && c.Section == section);
            if (classObj == null) return new List<Student>();

            // 2. Get all students in this class with their Fees included
            var students = _context.Students
                .Include(s => s.Class)
                .Include(s => s.StudentFees) // Load fees to calculate dues
                .ThenInclude(sf => sf.FeeStructure)
                .Where(s => s.ClassId == classObj.Id)
                .ToList();

            // 3. AUTO-GENERATE Missing Fees (Batch Processing)
            // If a "Tuition Fee" exists for the class, but Student X doesn't have a bill for it yet,
            // create the bill now so the "Pending Dues" red bubble is accurate.
            var classFees = _context.FeeStructures.Where(f => f.ClassId == classObj.Id).ToList();
            bool changesMade = false;

            foreach (var student in students)
            {
                foreach (var feeStruct in classFees)
                {
                    // Check if this student is missing this fee record
                    if (!student.StudentFees.Any(sf => sf.FeeStructureId == feeStruct.Id))
                    {
                        var newFee = new StudentFee
                        {
                            StudentId = student.Id,
                            FeeStructureId = feeStruct.Id,
                            AmountPaid = 0,
                            Status = "Unpaid"
                        };

                        _context.StudentFees.Add(newFee);

                        // Add to the local list so the UI sees it immediately without reloading DB
                        student.StudentFees.Add(newFee);

                        changesMade = true;
                    }
                }
            }

            if (changesMade)
            {
                _context.SaveChanges();
            }

            return students;
        }
        // ---------------------------------------------------------

        public List<Student> SearchStudents(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<Student>();

            return _context.Students
                .Include(s => s.Class)
                .Include(s => s.StudentFees) // Included fees here too for consistency
                .ThenInclude(sf => sf.FeeStructure)
                .Where(s => s.FullName.Contains(query) || s.ContactNumber.Contains(query))
                .ToList();
        }

        public List<StudentFee> GetStudentFees(int studentId)
        {
            var student = _context.Students.Find(studentId);
            if (student == null) return new List<StudentFee>();

            // 1. Get all fees defined for this student's class
            var classFees = _context.FeeStructures.Where(f => f.ClassId == student.ClassId).ToList();

            // 2. Get fees already assigned to this student
            var existingFees = _context.StudentFees
                .Where(sf => sf.StudentId == studentId)
                .Include(sf => sf.FeeStructure)
                .ToList();

            // 3. Lazy Loading: If a class fee exists but the student doesn't have it, create it now.
            bool changesMade = false;
            foreach (var feeStruct in classFees)
            {
                if (!existingFees.Any(ef => ef.FeeStructureId == feeStruct.Id))
                {
                    var newStudentFee = new StudentFee
                    {
                        StudentId = studentId,
                        FeeStructureId = feeStruct.Id,
                        AmountPaid = 0,
                        Status = "Unpaid"
                    };
                    _context.StudentFees.Add(newStudentFee);
                    changesMade = true;
                }
            }

            if (changesMade)
            {
                _context.SaveChanges();
                // Reload to get the new data with proper links
                return _context.StudentFees
                    .Where(sf => sf.StudentId == studentId)
                    .Include(sf => sf.FeeStructure)
                    .ToList();
            }

            return existingFees;
        }

        public void ProcessPayment(int studentFeeId, decimal amount, string mode)
        {
            var studentFee = _context.StudentFees.Include(sf => sf.FeeStructure).FirstOrDefault(x => x.Id == studentFeeId);
            if (studentFee == null) return;

            // 1. Create Transaction
            var transaction = new Transaction
            {
                StudentFeeId = studentFeeId,
                AmountPaid = amount,
                PaymentMode = mode,
                PaymentDate = DateTime.Now
            };
            _context.Transactions.Add(transaction);

            // 2. Update Student Balance
            studentFee.AmountPaid += amount;

            if (studentFee.AmountPaid >= studentFee.FeeStructure.Amount)
                studentFee.Status = "Paid";
            else
                studentFee.Status = "Partial";

            _context.SaveChanges();
        }
    }
}