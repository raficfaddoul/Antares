using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("FlagTypeDefectType", Schema = "adbo")]
public partial class FlagTypeDefectType
{
    [Key]
    [Column("FlagTypeDefectTypeID")]
    public Guid FlagTypeDefectTypeId { get; set; }

    [Column("FlagTypeID")]
    public Guid FlagTypeId { get; set; }

    [Column("DefectTypeID")]
    public Guid DefectTypeId { get; set; }

    [ForeignKey("DefectTypeId")]
    [InverseProperty("FlagTypeDefectTypes")]
    public virtual DefectType DefectType { get; set; } = null!;

    [ForeignKey("FlagTypeId")]
    [InverseProperty("FlagTypeDefectTypes")]
    public virtual FlagType FlagType { get; set; } = null!;
}
