using System.Collections.Generic;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IFeeService
    {
        void AddFeeStructure(FeeStructure fee);
        void DeleteFeeStructure(int feeId);
        List<FeeStructure> GetFeesByClass(int classId);
        List<Class> GetAllClasses();
    }
}