using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeTypeElementType", Schema = "adbo")]
public partial class AttributeTypeElementType
{
    [Key]
    [Column("AttributeTypeElementTypeID")]
    public Guid AttributeTypeElementTypeId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeTypeElementTypes")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("ElementTypeId")]
    [InverseProperty("AttributeTypeElementTypes")]
    public virtual ElementType ElementType { get; set; } = null!;
}
