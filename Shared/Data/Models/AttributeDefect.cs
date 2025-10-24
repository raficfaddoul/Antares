using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("AttributeDefect", Schema = "adbo")]
public partial class AttributeDefect
{
    [Key]
    [Column("AttributeDefectID")]
    public Guid AttributeDefectId { get; set; }

    public Guid? AttributeDefectGuid { get; set; }

    [Column("DefectID")]
    public Guid DefectId { get; set; }

    [Column("AttributeTypeID")]
    public Guid AttributeTypeId { get; set; }

    public DateOnly? Date { get; set; }

    [StringLength(50)]
    public string? Value { get; set; }

    [ForeignKey("AttributeTypeId")]
    [InverseProperty("AttributeDefects")]
    public virtual AttributeType AttributeType { get; set; } = null!;

    [ForeignKey("DefectId")]
    [InverseProperty("AttributeDefects")]
    public virtual Defect Defect { get; set; } = null!;
}
