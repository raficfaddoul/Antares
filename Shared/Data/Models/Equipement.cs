using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Equipement")]
public partial class Equipement
{
    [Key]
    [Column("EquipementID")]
    public Guid EquipementId { get; set; }

    [Column("EquipementTypeID")]
    public Guid EquipementTypeId { get; set; }

    [Column("EventID")]
    public Guid? EventId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [Column("ElementID")]
    public Guid? ElementId { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Z { get; set; }

    public double? ProjectionParameter { get; set; }

    [InverseProperty("Equipement")]
    public virtual ICollection<AttributeEquipement> AttributeEquipements { get; set; } = new List<AttributeEquipement>();

    [ForeignKey("ElementId")]
    [InverseProperty("Equipements")]
    public virtual Element? Element { get; set; }

    [ForeignKey("EquipementTypeId")]
    [InverseProperty("Equipements")]
    public virtual EquipementType EquipementType { get; set; } = null!;

    [ForeignKey("EventId")]
    [InverseProperty("Equipements")]
    public virtual Event? Event { get; set; }
}
