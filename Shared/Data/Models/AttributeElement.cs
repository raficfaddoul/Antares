using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeElement", Schema = "adbo")]
public partial class AttributeElement
{
    [Key]
    [Column("AttributeElementID")]
    public Guid AttributeElementId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    public DateOnly? Date { get; set; }

    [StringLength(50)]
    public string? Value { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeElements")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("ElementId")]
    [InverseProperty("AttributeElements")]
    public virtual Element Element { get; set; } = null!;
}
