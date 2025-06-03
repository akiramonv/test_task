using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace test_task.Models
{
    // Берем данный из таблицы column_data, схемы public 
    [Table("column_data", Schema = "public")]
    public class ColumnData
    {
        [Key] // Первичный ключ
        [Column("id")] 
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] //автогенерация значения со стороны СУБД
        public long Id { get; set; }

        [Column("data_text")]
        public string DataText { get; set; }

        [Column("column_name")]
        public long? ColumnNameId { get; set; }

        [ForeignKey("ColumnNameId")] // Внешний ключ 
        public virtual ColumnName ColumnName { get; set; }
    }
}
