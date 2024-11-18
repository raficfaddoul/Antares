using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureTypeFlagType", Schema = "adbo")]
public partial class StructureTypeFlagType
{
    [Key]
    [Column("StructureTypeFlagTypeID")]
    public Guid StructureTypeFlagTypeId { get; set; }

    [Column("StructureTypeID")]
    public Guid StructureTypeId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [ForeignKey("FlagTypeId")]
    [InverseProperty("StructureTypeFlagTypes")]
    public virtual FlagType FlagType { get; set; } = null!;

    [ForeignKey("StructureTypeId")]
    [InverseProperty("StructureTypeFlagTypes")]
    public virtual StructureType StructureType { get; set; } = null!;
}
