using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ElementTypeDefectType", Schema = "adbo")]
public partial class ElementTypeDefectType
{
    [Key]
    [Column("ElementTypeDefectTypeID")]
    public Guid ElementTypeDefectTypeId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [Column("DefectTypeID")]
    public Guid DefectTypeId { get; set; }

    [ForeignKey("DefectTypeId")]
    [InverseProperty("ElementTypeDefectTypes")]
    public virtual DefectType DefectType { get; set; } = null!;

    [ForeignKey("ElementTypeId")]
    [InverseProperty("ElementTypeDefectTypes")]
    public virtual ElementType ElementType { get; set; } = null!;
}
