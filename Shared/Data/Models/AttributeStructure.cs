using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeStructure", Schema = "adbo")]
public partial class AttributeStructure
{
    [Key]
    [Column("AttributeStructureID")]
    public Guid AttributeStructureId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [Column("AttributeTypeID")]
    public Guid? AttributeTypeId { get; set; }

    [StringLength(50)]
    public string? Value { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeStructures")]
    public virtual AttributeType? AttributeType { get; set; }

    [ForeignKey("StructureId")]
    [InverseProperty("AttributeStructures")]
    public virtual Structure Structure { get; set; } = null!;
}
