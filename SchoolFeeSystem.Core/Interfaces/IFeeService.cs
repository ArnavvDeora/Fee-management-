using System.Collections.Generic;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IFeeService
    {
        void AddFeeStructure(FeeStructure fee);
        List<FeeStructure> GetFeesByClass(int classId);
        List<Class> GetAllClasses();
    }
}