using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("DynamicPointLoadElement", Schema = "adbo")]
public partial class DynamicPointLoadElement
{
    [Key]
    [Column("DynamicPointLoadElementID")]
    public Guid DynamicPointLoadElementId { get; set; }

    [Column("DynamicPointLoadID")]
    public Guid DynamicPointLoadId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [ForeignKey("DynamicPointLoadId")]
    [InverseProperty("DynamicPointLoadElements")]
    public virtual DynamicPointLoad DynamicPointLoad { get; set; } = null!;

    [ForeignKey("ElementId")]
    [InverseProperty("DynamicPointLoadElements")]
    public virtual Element Element { get; set; } = null!;
}
