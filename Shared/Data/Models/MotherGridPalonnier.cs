using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("MotherGridPalonnier")]
public partial class MotherGridPalonnier
{
    [Key]
    [Column("MotherGridPalonnierID")]
    public Guid MotherGridPalonnierId { get; set; }

    [Column("EventMPScenarioID")]
    public Guid EventMpscenarioId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public int? Numero { get; set; }

    public bool Famille { get; set; }

    [StringLength(50)]
    public string? Type { get; set; }

    public double? X1 { get; set; }

    public double? Y1 { get; set; }

    public double? X2 { get; set; }

    public double? Y2 { get; set; }

    [InverseProperty("MotherGridPalonnier")]
    public virtual ICollection<ChargeMp> ChargeMps { get; set; } = new List<ChargeMp>();

    [ForeignKey("ElementTypeId")]
    [InverseProperty("MotherGridPalonniers")]
    public virtual ElementType ElementType { get; set; } = null!;

    [ForeignKey("EventMpscenarioId")]
    [InverseProperty("MotherGridPalonniers")]
    public virtual EventMpscenario EventMpscenario { get; set; } = null!;
}
