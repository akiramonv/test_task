using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace test_task.Models
{
    [Table("column_data", Schema = "public")]
    public class ColumnData
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("data_text")]
        public string DataText { get; set; }

        [Column("column_name")]
        public long? ColumnNameId { get; set; }

        [ForeignKey("ColumnNameId")]
        public virtual ColumnName ColumnName { get; set; }
    }
}
