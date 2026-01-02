using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System.Linq;

// Ensure BCrypt is available
using BCrypt.Net;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;

        public AuthService(AppDbContext context)
        {
            _context = context;
        }

        public User Login(string username, string password)
        {
            // 1. Find user by username
            var user = _context.Users.SingleOrDefault(u => u.Username == username);

            // 2. User not found? Fail.
            if (user == null) return null;

            // 3. Verify Password
            // OLD WAY (Broken): if (user.PasswordHash == password) 
            // NEW WAY (Fixed): Decrypts the hash and compares it
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

            if (isPasswordValid)
            {
                return user;
            }

            return null; // Password wrong
        }
    }
}