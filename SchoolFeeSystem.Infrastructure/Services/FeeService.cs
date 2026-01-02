using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class FeeService : IFeeService
    {
        private readonly AppDbContext _context;

        public FeeService(AppDbContext context)
        {
            _context = context;
        }

        public void AddFeeStructure(FeeStructure fee)
        {
            _context.FeeStructures.Add(fee);
            _context.SaveChanges();
        }
        public void DeleteFeeStructure(int feeId)
        {
            var fee = _context.FeeStructures.Find(feeId);
            if (fee != null)
            {
                // Optional: Remove linked student fees if nobody has paid yet
                var linkedFees = _context.StudentFees.Where(sf => sf.FeeStructureId == feeId).ToList();
                _context.StudentFees.RemoveRange(linkedFees);

                _context.FeeStructures.Remove(fee);
                _context.SaveChanges();
            }
        }
        public List<FeeStructure> GetFeesByClass(int classId)
        {
            return _context.FeeStructures
                .Where(f => f.ClassId == classId)
                .Include(f => f.Class)
                .ToList();
        }

        public List<Class> GetAllClasses()
        {
            return _context.Classes.ToList();
        }
    }
}