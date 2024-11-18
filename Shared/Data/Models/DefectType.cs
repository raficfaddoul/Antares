using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("DefectType", Schema = "adbo")]
public partial class DefectType
{
    [Key]
    [Column("DefectTypeID")]
    public Guid DefectTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [Column(TypeName = "image")]
    public byte[]? Icon { get; set; }

    [InverseProperty("DefectType")]
    public virtual ICollection<AttributeTypeDefectType> AttributeTypeDefectTypes { get; } = new List<AttributeTypeDefectType>();

    [InverseProperty("DefectType")]
    public virtual ICollection<Defect> Defects { get; } = new List<Defect>();

    [InverseProperty("DefectType")]
    public virtual ICollection<ElementTypeDefectType> ElementTypeDefectTypes { get; } = new List<ElementTypeDefectType>();

    [InverseProperty("DefectType")]
    public virtual ICollection<FlagTypeDefectType> FlagTypeDefectTypes { get; } = new List<FlagTypeDefectType>();
}
