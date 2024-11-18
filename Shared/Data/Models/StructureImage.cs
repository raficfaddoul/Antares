using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureImage", Schema = "adbo")]
public partial class StructureImage
{
    [Key]
    [Column("StructureImageID")]
    public Guid StructureImageId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [ForeignKey("StructureId")]
    [InverseProperty("StructureImages")]
    public virtual Structure Structure { get; set; } = null!;
}
