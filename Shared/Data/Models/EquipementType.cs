using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("EquipementType")]
public partial class EquipementType
{
    [Key]
    [Column("EquipementTypeID")]
    public Guid EquipementTypeId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [Column(TypeName = "image")]
    public byte[]? Icon { get; set; }

    public double? Weight { get; set; }

    public int? Inventory { get; set; }

    [InverseProperty("EquipementType")]
    public virtual ICollection<EquipementTypeAttributeType> EquipementTypeAttributeTypes { get; set; } = new List<EquipementTypeAttributeType>();

    [InverseProperty("EquipementType")]
    public virtual ICollection<Equipement> Equipements { get; set; } = new List<Equipement>();
}
