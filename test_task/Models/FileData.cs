using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace test_task.Models
{
    [Table("file_data", Schema = "public")]
    public class FileData
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("type_id")]
        public long? FileTypeId { get; set; }

        [Column("data_id")]
        public long? DataId { get; set; }

        [ForeignKey("FileTypeId")]
        public virtual FileType FileType { get; set; }

        [ForeignKey("DataId")]
        public virtual ColumnData ColumnData { get; set; }
    }
}