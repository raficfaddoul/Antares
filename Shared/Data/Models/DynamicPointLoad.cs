using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("DynamicPointLoad", Schema = "adbo")]
public partial class DynamicPointLoad
{
    [Key]
    [Column("DynamicPointLoadID")]
    public Guid DynamicPointLoadId { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    public string? Description { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    public double? Value { get; set; }

    public double? ApplicationPointDistance { get; set; }

    [Column(TypeName = "xml")]
    public string? TemporalFunc { get; set; }

    [InverseProperty("DynamicPointLoad")]
    public virtual ICollection<DynamicPointLoadElement> DynamicPointLoadElements { get; } = new List<DynamicPointLoadElement>();

    [ForeignKey("LoadCaseId")]
    [InverseProperty("DynamicPointLoads")]
    public virtual LoadCase LoadCase { get; set; } = null!;
}
