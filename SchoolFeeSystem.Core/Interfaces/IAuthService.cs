using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Core.Interfaces
{
    public interface IAuthService
    {
        // Tries to login. Returns the User if successful, or null if failed.
        User? Login(string username, string password);
    }
}