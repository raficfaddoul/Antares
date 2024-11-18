using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureTypeAttributeType", Schema = "adbo")]
public partial class StructureTypeAttributeType
{
    [Key]
    [Column("StructureTypeAttributeTypeID")]
    public Guid StructureTypeAttributeTypeId { get; set; }

    [Column("StructureTypeID")]
    public Guid StructureTypeId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("StructureTypeAttributeTypes")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("StructureTypeId")]
    [InverseProperty("StructureTypeAttributeTypes")]
    public virtual StructureType StructureType { get; set; } = null!;
}
