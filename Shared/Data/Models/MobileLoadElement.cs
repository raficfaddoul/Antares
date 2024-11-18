using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("MobileLoadElement", Schema = "adbo")]
public partial class MobileLoadElement
{
    [Key]
    [Column("MobileLoadElementID")]
    public Guid MobileLoadElementId { get; set; }

    [Column("MobileLoadID")]
    public Guid MobileLoadId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [ForeignKey("ElementId")]
    [InverseProperty("MobileLoadElements")]
    public virtual Element Element { get; set; } = null!;

    [ForeignKey("MobileLoadId")]
    [InverseProperty("MobileLoadElements")]
    public virtual MobileLoad MobileLoad { get; set; } = null!;
}
