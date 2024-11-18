using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureTypeInspectionResultType", Schema = "adbo")]
public partial class StructureTypeInspectionResultType
{
    [Key]
    [Column("StructureTypeInspectionResultTypeID")]
    public Guid StructureTypeInspectionResultTypeId { get; set; }

    [Column("StructureTypeID")]
    public Guid StructureTypeId { get; set; }

    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [ForeignKey("InspectionResultTypeId")]
    [InverseProperty("StructureTypeInspectionResultTypes")]
    public virtual InspectionResultType InspectionResultType { get; set; } = null!;

    [ForeignKey("StructureTypeId")]
    [InverseProperty("StructureTypeInspectionResultTypes")]
    public virtual StructureType StructureType { get; set; } = null!;
}
