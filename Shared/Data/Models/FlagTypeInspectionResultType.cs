using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagTypeInspectionResultType", Schema = "adbo")]
public partial class FlagTypeInspectionResultType
{
    [Key]
    [Column("FlagTypeInspectionResultTypeID")]
    public Guid FlagTypeInspectionResultTypeId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [ForeignKey("FlagTypeId")]
    [InverseProperty("FlagTypeInspectionResultTypes")]
    public virtual FlagType FlagType { get; set; } = null!;

    [ForeignKey("InspectionResultTypeId")]
    [InverseProperty("FlagTypeInspectionResultTypes")]
    public virtual InspectionResultType InspectionResultType { get; set; } = null!;
}
