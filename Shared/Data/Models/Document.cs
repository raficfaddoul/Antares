using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Document", Schema = "adbo")]
public partial class Document
{
    [Key]
    [Column("DocumentID")]
    public Guid DocumentId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [ForeignKey("StructureId")]
    [InverseProperty("Documents")]
    public virtual Structure Structure { get; set; } = null!;
}
