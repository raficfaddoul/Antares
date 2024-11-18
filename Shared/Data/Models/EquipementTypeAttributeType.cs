using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("EquipementTypeAttributeType")]
public partial class EquipementTypeAttributeType
{
    [Key]
    [Column("EquipementTypeAttributeTypeID")]
    public Guid EquipementTypeAttributeTypeId { get; set; }

    [Column("EquipementTypeID")]
    public Guid EquipementTypeId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("EquipementTypeAttributeTypes")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("EquipementTypeId")]
    [InverseProperty("EquipementTypeAttributeTypes")]
    public virtual EquipementType EquipementType { get; set; } = null!;
}
