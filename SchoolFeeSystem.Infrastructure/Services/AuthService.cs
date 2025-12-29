using System.Linq;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using SchoolFeeSystem.Infrastructure.Security;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;

        public AuthService(AppDbContext context)
        {
            _context = context;
        }

        public User? Login(string username, string password)
        {
            // 1. Find user by username
            var user = _context.Users.SingleOrDefault(u => u.Username == username);
            if (user == null) return null;

            // 2. Verify password using our Argon2 helper
            bool isValid = PasswordHasher.VerifyPassword(password, user.PasswordHash, user.Salt);

            return isValid ? user : null;
        }
    }
}