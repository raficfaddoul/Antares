using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Defect", Schema = "adbo")]
public partial class Defect
{
    [Key]
    [Column("DefectID")]
    public Guid DefectId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [Column("DefectTypeID")]
    public Guid DefectTypeId { get; set; }

    [Column("ElementTypeDefectTypeID")]
    public int? ElementTypeDefectTypeId { get; set; }

    public string? Description { get; set; }

    public DateOnly? Date { get; set; }

    [InverseProperty("Defect")]
    public virtual ICollection<AttributeDefect> AttributeDefects { get; set; } = new List<AttributeDefect>();

    [ForeignKey("DefectTypeId")]
    [InverseProperty("Defects")]
    public virtual DefectType DefectType { get; set; } = null!;

    [ForeignKey("ElementId")]
    [InverseProperty("Defects")]
    public virtual Element Element { get; set; } = null!;
}
