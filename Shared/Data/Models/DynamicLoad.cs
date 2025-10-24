using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("DynamicLoad", Schema = "adbo")]
public partial class DynamicLoad
{
    [Key]
    [Column("DynamicLoadID")]
    public Guid DynamicLoadId { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    public string? Description { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    [Column(TypeName = "xml")]
    public string? TemporalFunc { get; set; }

    public double? StartValue { get; set; }

    public double? EndValue { get; set; }

    [Column("DOF")]
    public int? Dof { get; set; }

    [InverseProperty("DynamicLoad")]
    public virtual ICollection<DynamicLoadElement> DynamicLoadElements { get; set; } = new List<DynamicLoadElement>();

    [ForeignKey("LoadCaseId")]
    [InverseProperty("DynamicLoads")]
    public virtual LoadCase LoadCase { get; set; } = null!;
}
