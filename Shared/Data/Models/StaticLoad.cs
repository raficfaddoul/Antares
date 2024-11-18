using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StaticLoad", Schema = "adbo")]
public partial class StaticLoad
{
    [Key]
    [Column("StaticLoadID")]
    public Guid StaticLoadId { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    public string? Description { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    public double? StartValue { get; set; }

    public double? EndValue { get; set; }

    [ForeignKey("LoadCaseId")]
    [InverseProperty("StaticLoads")]
    public virtual LoadCase LoadCase { get; set; } = null!;

    [InverseProperty("StaticLoad")]
    public virtual ICollection<StaticLoadElement> StaticLoadElements { get; } = new List<StaticLoadElement>();
}
