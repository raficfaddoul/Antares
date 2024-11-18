using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("DynamicLoadElement", Schema = "adbo")]
public partial class DynamicLoadElement
{
    [Key]
    [Column("DynamicLoadElementID")]
    public Guid DynamicLoadElementId { get; set; }

    [Column("DynamicLoadID")]
    public Guid DynamicLoadId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [ForeignKey("DynamicLoadId")]
    [InverseProperty("DynamicLoadElements")]
    public virtual DynamicLoad DynamicLoad { get; set; } = null!;

    [ForeignKey("ElementId")]
    [InverseProperty("DynamicLoadElements")]
    public virtual Element Element { get; set; } = null!;
}
