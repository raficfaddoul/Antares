using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureType", Schema = "adbo")]
public partial class StructureType
{
    [Key]
    [Column("StructureTypeID")]
    public Guid StructureTypeId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [InverseProperty("StructureType")]
    public virtual ICollection<StructureTypeAttributeType> StructureTypeAttributeTypes { get; } = new List<StructureTypeAttributeType>();

    [InverseProperty("StructureType")]
    public virtual ICollection<StructureTypeFlagType> StructureTypeFlagTypes { get; } = new List<StructureTypeFlagType>();

    [InverseProperty("StructureType")]
    public virtual ICollection<StructureTypeInspectionResultType> StructureTypeInspectionResultTypes { get; } = new List<StructureTypeInspectionResultType>();

    [InverseProperty("StructureType")]
    public virtual ICollection<Structure> Structures { get; } = new List<Structure>();
}
