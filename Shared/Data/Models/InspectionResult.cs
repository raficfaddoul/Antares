using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("InspectionResult", Schema = "adbo")]
public partial class InspectionResult
{
    [Key]
    [Column("InspectionResultID")]
    public Guid InspectionResultId { get; set; }

    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [Column("InspectionID")]
    public Guid? InspectionId { get; set; }

    [Column(TypeName = "xml")]
    public string? Result { get; set; }

    public float? ResultNumeric { get; set; }

    public string? Comments { get; set; }

    [Column(TypeName = "date")]
    public DateTime? Date { get; set; }

    [ForeignKey("ElementId")]
    [InverseProperty("InspectionResults")]
    public virtual Element Element { get; set; } = null!;

    [ForeignKey("InspectionId")]
    [InverseProperty("InspectionResults")]
    public virtual Inspection? Inspection { get; set; }

    [ForeignKey("InspectionResultTypeId")]
    [InverseProperty("InspectionResults")]
    public virtual InspectionResultType InspectionResultType { get; set; } = null!;
}
