using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeType", Schema = "adbo")]
public partial class AttributeType
{
    [Key]
    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [StringLength(50)]
    public string? Units { get; set; }

    [StringLength(50)]
    public string? Format { get; set; }

    [StringLength(1000)]
    public string? FileAddr { get; set; }

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeDefect> AttributeDefects { get; set; } = new List<AttributeDefect>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeElement> AttributeElements { get; set; } = new List<AttributeElement>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeEquipement> AttributeEquipements { get; set; } = new List<AttributeEquipement>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeStructure> AttributeStructures { get; set; } = new List<AttributeStructure>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeTypeDefectType> AttributeTypeDefectTypes { get; set; } = new List<AttributeTypeDefectType>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<AttributeTypeElementType> AttributeTypeElementTypes { get; set; } = new List<AttributeTypeElementType>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<EquipementTypeAttributeType> EquipementTypeAttributeTypes { get; set; } = new List<EquipementTypeAttributeType>();

    [InverseProperty("AttributeType")]
    public virtual ICollection<StructureTypeAttributeType> StructureTypeAttributeTypes { get; set; } = new List<StructureTypeAttributeType>();
}
