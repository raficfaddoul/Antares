using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Axe")]
public partial class Axe
{
    [Key]
    [Column("AxeID")]
    public Guid AxeId { get; set; }

    [Column("EventID")]
    public Guid EventId { get; set; }

    [ForeignKey("EventId")]
    [InverseProperty("Axes")]
    public virtual Event Event { get; set; } = null!;
}
