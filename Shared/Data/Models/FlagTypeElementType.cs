using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagTypeElementType", Schema = "adbo")]
public partial class FlagTypeElementType
{
    [Key]
    [Column("FlagTypeElementTypeID")]
    public Guid FlagTypeElementTypeId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [ForeignKey("ElementTypeId")]
    [InverseProperty("FlagTypeElementTypes")]
    public virtual ElementType ElementType { get; set; } = null!;

    [ForeignKey("FlagTypeId")]
    [InverseProperty("FlagTypeElementTypes")]
    public virtual FlagType FlagType { get; set; } = null!;
}
