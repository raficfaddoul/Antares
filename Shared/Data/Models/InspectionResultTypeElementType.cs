using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("InspectionResultTypeElementType", Schema = "adbo")]
public partial class InspectionResultTypeElementType
{
    [Key]
    [Column("InspectionResultTypeElementTypeID")]
    public Guid InspectionResultTypeElementTypeId { get; set; }

    [Column("InspectionResultTypeID")]
    public Guid InspectionResultTypeId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [ForeignKey("ElementTypeId")]
    [InverseProperty("InspectionResultTypeElementTypes")]
    public virtual ElementType ElementType { get; set; } = null!;

    [ForeignKey("InspectionResultTypeId")]
    [InverseProperty("InspectionResultTypeElementTypes")]
    public virtual InspectionResultType InspectionResultType { get; set; } = null!;
}
