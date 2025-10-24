using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureInspectionResult", Schema = "adbo")]
public partial class StructureInspectionResult
{
    [Key]
    [Column("StructureInspectionResultID")]
    public Guid StructureInspectionResultId { get; set; }

    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [Column(TypeName = "xml")]
    public string? Result { get; set; }

    public float? ResultNumeric { get; set; }

    public string? Comments { get; set; }

    public DateOnly? Date { get; set; }

    [ForeignKey("InspectionResultTypeId")]
    [InverseProperty("StructureInspectionResults")]
    public virtual InspectionResultType InspectionResultType { get; set; } = null!;

    [ForeignKey("StructureId")]
    [InverseProperty("StructureInspectionResults")]
    public virtual Structure Structure { get; set; } = null!;
}
