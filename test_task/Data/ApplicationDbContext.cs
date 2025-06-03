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

        //набор сущностей ColumnName, который EF Core будет отображать в таблицу column_name
        public DbSet<ColumnName> ColumnNames { get; set; }

        //набор сущностей ColumnData, для таблицы column_data
        public DbSet<ColumnData> ColumnDatas { get; set; }

        public DbSet<Company> Companies { get; set; }
        public DbSet<FileType> FileTypes { get; set; }
        public DbSet<FileData> FileDatas { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ColumnName>()
                .HasMany(cn => cn.ColumnDatas)//указывает, что ColumnName обладает коллекцией ColumnDatas
                .WithOne(cd => cd.ColumnName)//каждая запись ColumnData ссылается на одну ColumnName через навигационное свойство ColumnName
                .HasForeignKey(cd => cd.ColumnNameId)//указывает, что внешним ключом является свойство ColumnNameId в ColumnData
                .OnDelete(DeleteBehavior.SetNull);// при удалении записи ColumnName во всех связанных ColumnData вместо каскадного удаления устанавливается ColumnNameId = NULL

            // Связь FileType с Company
            modelBuilder.Entity<FileType>()
                .HasOne(ft => ft.Company)
                .WithMany()
                .HasForeignKey(ft => ft.CompanyId)
                .OnDelete(DeleteBehavior.SetNull);

            // Связь FileData с FileType
            modelBuilder.Entity<FileData>()
                .HasOne(fd => fd.FileType)
                .WithMany()
                .HasForeignKey(fd => fd.FileTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            // Связь FileData с ColumnData
            modelBuilder.Entity<FileData>()
                .HasOne(fd => fd.ColumnData)
                .WithMany()
                .HasForeignKey(fd => fd.DataId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
