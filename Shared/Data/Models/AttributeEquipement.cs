using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeEquipement")]
public partial class AttributeEquipement
{
    [Key]
    [Column("AttributeEquipementID")]
    public Guid AttributeEquipementId { get; set; }

    [Column("EquipementID")]
    public Guid EquipementId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [StringLength(50)]
    public string? Value { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeEquipements")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("EquipementId")]
    [InverseProperty("AttributeEquipements")]
    public virtual Equipement Equipement { get; set; } = null!;
}
