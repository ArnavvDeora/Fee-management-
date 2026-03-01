using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using System.Linq;

namespace SchoolFeeSystem.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        // ====================================================
        // SCHOOL FEE TABLES
        // ====================================================
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

        // ====================================================
        // HR / PAYROLL TABLES
        // ====================================================
        public DbSet<Employee> Employees { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        public DbSet<SalaryRecord> SalaryRecords { get; set; }
        public DbSet<SalaryComponent> SalaryComponents { get; set; }
        public DbSet<OvertimeAllowance> OvertimeAllowances { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<Salary> Salaries { get; set; }
        public DbSet<CompanyGatePass> CompanyGatePasses { get; set; }
        public DbSet<FlaggedBiometricEntry> FlaggedBiometricEntries { get; set; }


        // ====================================================
        // CONSTRUCTOR - NO DB ACCESS, MIGRATION ONLY
        // ====================================================
        public AppDbContext()
        {
            // ✅ This runs all pending migrations on startup
            // Creates the .db file and ALL tables if they don't exist
            //Database.Migrate();
            //Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // ✅ FIXED: Print the path so you can verify which DB is being used
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var appFolder = System.IO.Path.Combine(path, "SchoolFeeSystem");

            if (!System.IO.Directory.Exists(appFolder))
                System.IO.Directory.CreateDirectory(appFolder);

            var dbPath = System.IO.Path.Join(appFolder, "school_fees.db");

            // ✅ DEBUG: This will print the exact DB path in Output window
            System.Diagnostics.Debug.WriteLine($"🗄️ DATABASE PATH: {dbPath}");

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Class>()
                .HasIndex(c => new { c.Name, c.Section })
                .IsUnique();

            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.BiometricId)
                .IsUnique();

            modelBuilder.Entity<Employee>()
                .Property(e => e.BaseSalary)
                .HasConversion<double>();

            base.OnModelCreating(modelBuilder);
        }

        // ====================================================
        // ✅ SEED DATA - Called from App.xaml.cs AFTER startup
        // ====================================================
        public void EnsureDefaultDataExists()
        {
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
                    System.Diagnostics.Debug.WriteLine("✅ Default admin user created");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Seed error: {ex.Message}");
            }
        }
    }
}