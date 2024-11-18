using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("InspectionResultType", Schema = "adbo")]
public partial class InspectionResultType
{
    [Key]
    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [InverseProperty("InspectionResultType")]
    public virtual ICollection<FlagTypeInspectionResultType> FlagTypeInspectionResultTypes { get; } = new List<FlagTypeInspectionResultType>();

    [InverseProperty("InspectionResultType")]
    public virtual ICollection<InspectionResultTypeElementType> InspectionResultTypeElementTypes { get; } = new List<InspectionResultTypeElementType>();

    [InverseProperty("InspectionResultType")]
    public virtual ICollection<InspectionResult> InspectionResults { get; } = new List<InspectionResult>();

    [InverseProperty("InspectionResultType")]
    public virtual ICollection<StructureInspectionResult> StructureInspectionResults { get; } = new List<StructureInspectionResult>();

    [InverseProperty("InspectionResultType")]
    public virtual ICollection<StructureTypeInspectionResultType> StructureTypeInspectionResultTypes { get; } = new List<StructureTypeInspectionResultType>();
}
