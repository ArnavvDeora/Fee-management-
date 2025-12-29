using System;
using System.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace SchoolFeeSystem.Infrastructure.Security
{
    public static class PasswordHasher
    {
        public static (string hash, string salt) HashPassword(string password)
        {
            var saltBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }

            using (var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password)))
            {
                argon2.Salt = saltBytes;
                argon2.DegreeOfParallelism = 8;
                argon2.MemorySize = 65536;
                argon2.Iterations = 4;

                var hashBytes = argon2.GetBytes(16);
                return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
            }
        }

        // Helper to verify (we will use this later)
        public static bool VerifyPassword(string password, string hash, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            using (var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password)))
            {
                argon2.Salt = saltBytes;
                argon2.DegreeOfParallelism = 8;
                argon2.MemorySize = 65536;
                argon2.Iterations = 4;

                var newHash = Convert.ToBase64String(argon2.GetBytes(16));
                return hash == newHash;
            }
        }
    }
}