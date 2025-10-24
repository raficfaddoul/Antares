using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StaticPointLoad", Schema = "adbo")]
public partial class StaticPointLoad
{
    [Key]
    [Column("StaticPointLoadID")]
    public Guid StaticPointLoadId { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    public string? Description { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    public double? Value { get; set; }

    public double? ApplicationPointDistance { get; set; }

    [Column("DOF")]
    public int? Dof { get; set; }

    [ForeignKey("LoadCaseId")]
    [InverseProperty("StaticPointLoads")]
    public virtual LoadCase LoadCase { get; set; } = null!;

    [InverseProperty("StaticPointLoad")]
    public virtual ICollection<StaticPointLoadElement> StaticPointLoadElements { get; set; } = new List<StaticPointLoadElement>();
}
