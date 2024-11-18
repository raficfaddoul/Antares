using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeTypeDefectType", Schema = "adbo")]
public partial class AttributeTypeDefectType
{
    [Key]
    [Column("AttributeTypeDefectTypeID")]
    public Guid AttributeTypeDefectTypeId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    [Column("DefectTypeID")]
    public Guid DefectTypeId { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeTypeDefectTypes")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("DefectTypeId")]
    [InverseProperty("AttributeTypeDefectTypes")]
    public virtual DefectType DefectType { get; set; } = null!;
}
