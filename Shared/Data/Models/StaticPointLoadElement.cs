using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StaticPointLoadElement", Schema = "adbo")]
public partial class StaticPointLoadElement
{
    [Key]
    [Column("StaticPointLoadElementID")]
    public Guid StaticPointLoadElementId { get; set; }

    [Column("StaticPointLoadID")]
    public Guid StaticPointLoadId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [ForeignKey("ElementId")]
    [InverseProperty("StaticPointLoadElements")]
    public virtual Element Element { get; set; } = null!;

    [ForeignKey("StaticPointLoadId")]
    [InverseProperty("StaticPointLoadElements")]
    public virtual StaticPointLoad StaticPointLoad { get; set; } = null!;
}
