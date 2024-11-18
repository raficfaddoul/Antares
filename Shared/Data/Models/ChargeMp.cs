using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ChargeMP")]
public partial class ChargeMp
{
    [Key]
    [Column("ChargeMPID")]
    public Guid ChargeMpid { get; set; }

    [Column("MotherGridPalonnierID")]
    public Guid MotherGridPalonnierId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public int? Numero { get; set; }

    public double? X { get; set; }

    public double? Charge { get; set; }

    [ForeignKey("MotherGridPalonnierId")]
    [InverseProperty("ChargeMps")]
    public virtual MotherGridPalonnier MotherGridPalonnier { get; set; } = null!;
}
