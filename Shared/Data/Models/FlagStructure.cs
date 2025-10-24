using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagStructure", Schema = "adbo")]
public partial class FlagStructure
{
    [Key]
    [Column("FlagStructureID")]
    public Guid FlagStructureId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    public DateOnly? Date { get; set; }

    [ForeignKey("FlagTypeId")]
    [InverseProperty("FlagStructures")]
    public virtual FlagType FlagType { get; set; } = null!;

    [ForeignKey("StructureId")]
    [InverseProperty("FlagStructures")]
    public virtual Structure Structure { get; set; } = null!;
}
