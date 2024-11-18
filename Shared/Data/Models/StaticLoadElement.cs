using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StaticLoadElement", Schema = "adbo")]
public partial class StaticLoadElement
{
    [Key]
    [Column("StaticLoadElementID")]
    public Guid StaticLoadElementId { get; set; }

    [Column("StaticLoadID")]
    public Guid StaticLoadId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [ForeignKey("ElementId")]
    [InverseProperty("StaticLoadElements")]
    public virtual Element Element { get; set; } = null!;

    [ForeignKey("StaticLoadId")]
    [InverseProperty("StaticLoadElements")]
    public virtual StaticLoad StaticLoad { get; set; } = null!;
}
