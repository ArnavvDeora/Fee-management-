using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using SchoolFeeSystem.Core.Entities;

namespace SchoolFeeSystem.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        private static string _dbPassword = "YourSecureRuntimePassword";

        public DbSet<User> Users { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Class> Classes { get; set; }
        public DbSet<FeeStructure> FeeStructures { get; set; }

        // UNCOMMENT THESE NOW:
        public DbSet<StudentFee> StudentFees { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionStringBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = "school_fees.db",
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    // Password = _dbPassword 
                };

                optionsBuilder.UseSqlite(connectionStringBuilder.ToString());
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<Class>()
        .HasIndex(c => new { c.Name, c.Section })
        .IsUnique();
        }

        public static void SetDbPassword(string password) => _dbPassword = password;
    }
}