using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using test_task.Models;

namespace test_task.Models
{
    [Table("column_name", Schema = "public")]
    public class ColumnName
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("name")]
        [StringLength(80)]
        public string Name { get; set; }

        public virtual ICollection<ColumnData> ColumnDatas { get; set; }
    }
}
