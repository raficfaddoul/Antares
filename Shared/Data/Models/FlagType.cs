using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagType", Schema = "adbo")]
public partial class FlagType
{
    [Key]
    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [StringLength(200)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [InverseProperty("FlagType")]
    public virtual ICollection<FlagElement> FlagElements { get; } = new List<FlagElement>();

    [InverseProperty("FlagType")]
    public virtual ICollection<FlagStructure> FlagStructures { get; } = new List<FlagStructure>();

    [InverseProperty("FlagType")]
    public virtual ICollection<FlagTypeDefectType> FlagTypeDefectTypes { get; } = new List<FlagTypeDefectType>();

    [InverseProperty("FlagType")]
    public virtual ICollection<FlagTypeElementType> FlagTypeElementTypes { get; } = new List<FlagTypeElementType>();

    [InverseProperty("FlagType")]
    public virtual ICollection<FlagTypeInspectionResultType> FlagTypeInspectionResultTypes { get; } = new List<FlagTypeInspectionResultType>();

    [InverseProperty("FlagType")]
    public virtual ICollection<StructureTypeFlagType> StructureTypeFlagTypes { get; } = new List<StructureTypeFlagType>();
}
