using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using System.Linq;

namespace SchoolFeeSystem.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        // Tables
        public DbSet<User> Users { get; set; }
        public DbSet<Class> Classes { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<FeeStructure> FeeStructures { get; set; }
        public DbSet<StudentFee> StudentFees { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Allowance> Allowances { get; set; }
        public DbSet<Deduction> Deductions { get; set; }
        public DbSet<SalaryRevision> SalaryRevisions { get; set; }
        public DbSet<Holiday> Holidays { get; set; }
        public DbSet<AttendanceSettings> AttendanceSettings { get; set; }

        // HR Tables
        public DbSet<Employee> Employees { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        public DbSet<SalaryRecord> SalaryRecords { get; set; }
        public DbSet<SalaryComponent> SalaryComponents { get; set; }
        public DbSet<OvertimeAllowance> OvertimeAllowances { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<Salary> Salaries { get; set; }

        public AppDbContext()
        {
            // ✅ FIXED: Ensure database is created before accessing tables
            Database.EnsureCreated();

            // ✅ FIXED: Wrapped in try-catch to prevent errors on first run
            try
            {
                if (!Users.Any())
                {
                    var admin = new User
                    {
                        Username = "admin",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                        Role = UserRole.Admin
                    };
                    Users.Add(admin);
                    SaveChanges();
                }
            }
            catch (System.Exception)
            {
                // Table doesn't exist yet - migrations will create it
                // This is normal on first run
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=school_fees.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // FIX 3: Changed 'c.ClassName' to 'c.Name' (This was causing the crash)
            modelBuilder.Entity<Class>()
                .HasIndex(c => new { c.Name, c.Section })
                .IsUnique();
            modelBuilder.Entity<Employee>().HasIndex(e => e.BiometricId).IsUnique();
            modelBuilder.Entity<Employee>()
                .Property(e => e.BaseSalary)
                .HasConversion<double>();

            base.OnModelCreating(modelBuilder);
        }
    }
}