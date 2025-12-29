using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Infrastructure.Security;
using SchoolFeeSystem.Infrastructure.Services;
using System.Linq;
namespace SchoolFeeSystem.Infrastructure.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            context.Database.EnsureCreated();

            // 1. Create SuperAdmin if not exists
            if (!context.Users.Any())
            {
                var (hash, salt) = PasswordHasher.HashPassword("Admin@123");
                var admin = new User
                {
                    Username = "admin",
                    PasswordHash = hash,
                    Salt = salt,
                    Role = UserRole.SuperAdmin
                };
                context.Users.Add(admin);
            }

            // 2. Pre-seed Classes (1st to 12th, Sections A-E)
            if (!context.Classes.Any())
            {
                string[] standards = { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th", "11th", "12th" };
                string[] sections = { "A", "B", "C", "D", "E" };

                foreach (var std in standards)
                {
                    foreach (var sec in sections)
                    {
                        context.Classes.Add(new Class { Name = std, Section = sec });
                    }
                }
            }

            context.SaveChanges();
        }
    }
}