using Microsoft.EntityFrameworkCore;
using test_task.Models;

namespace test_task.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ColumnName> ColumnNames { get; set; }
        public DbSet<ColumnData> ColumnDatas { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ColumnName>()
                .HasMany(cn => cn.ColumnDatas)
                .WithOne(cd => cd.ColumnName)
                .HasForeignKey(cd => cd.ColumnNameId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
