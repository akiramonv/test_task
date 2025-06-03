using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace test_task.Models
{
    // Берем данный из таблицы column_name, схемы public 
    [Table("column_name", Schema = "public")]
    public class ColumnName
    {
        [Key] // Первичный ключ
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] //автогенерация значения со стороны СУБД
        public long Id { get; set; }

        [Column("name")]
        [StringLength(80)] //ограничения в 80 символов 
        public string Name { get; set; }

        /* Навигационное свойство: все данные, относящиеся к этой колонке.
        У одной «имени столбца» (ColumnName) может быть много записей «данных столбца» (ColumnData)*/
        public virtual ICollection<ColumnData> ColumnDatas { get; set; }
    }
}
