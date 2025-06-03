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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ColumnName>()
                .HasMany(cn => cn.ColumnDatas)//указывает, что ColumnName обладает коллекцией ColumnDatas
                .WithOne(cd => cd.ColumnName)//каждая запись ColumnData ссылается на одну ColumnName через навигационное свойство ColumnName
                .HasForeignKey(cd => cd.ColumnNameId)//указывает, что внешним ключом является свойство ColumnNameId в ColumnData
                .OnDelete(DeleteBehavior.SetNull);// при удалении записи ColumnName во всех связанных ColumnData вместо каскадного удаления устанавливается ColumnNameId = NULL
        }
    }
}
