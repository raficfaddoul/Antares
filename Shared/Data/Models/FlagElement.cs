using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagElement", Schema = "adbo")]
public partial class FlagElement
{
    [Key]
    [Column("FlagElementID")]
    public Guid FlagElementId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    public DateOnly? Date { get; set; }

    [StringLength(50)]
    public string? Value { get; set; }

    [ForeignKey("ElementId")]
    [InverseProperty("FlagElements")]
    public virtual Element Element { get; set; } = null!;

    [ForeignKey("FlagTypeId")]
    [InverseProperty("FlagElements")]
    public virtual FlagType FlagType { get; set; } = null!;
}
