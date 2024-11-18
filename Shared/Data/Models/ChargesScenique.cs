using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ChargesScenique")]
public partial class ChargesScenique
{
    [Key]
    [Column("ChargesSceniqueID")]
    public Guid ChargesSceniqueId { get; set; }

    [Column("EventID")]
    public Guid EventId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public int? Numero { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Charge { get; set; }

    [ForeignKey("EventId")]
    [InverseProperty("ChargesSceniques")]
    public virtual Event Event { get; set; } = null!;
}
