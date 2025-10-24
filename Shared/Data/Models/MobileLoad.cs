using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("MobileLoad", Schema = "adbo")]
public partial class MobileLoad
{
    [Key]
    [Column("MobileLoadID")]
    public Guid MobileLoadId { get; set; }

    public string? Description { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    [Column("ValueVL")]
    public double? ValueVl { get; set; }

    [Column("ValuePL")]
    public double? ValuePl { get; set; }

    public double? ValueConvoi { get; set; }

    [Column("SpeedVL")]
    public double? SpeedVl { get; set; }

    [Column("SpeedPL")]
    public double? SpeedPl { get; set; }

    public double? AverageHourlyTraffic { get; set; }

    [Column("PercentagePL")]
    public double? PercentagePl { get; set; }

    public double? LoadUncertainty { get; set; }

    public double? SpeedUncertainty { get; set; }

    [ForeignKey("LoadCaseId")]
    [InverseProperty("MobileLoads")]
    public virtual LoadCase LoadCase { get; set; } = null!;

    [InverseProperty("MobileLoad")]
    public virtual ICollection<MobileLoadElement> MobileLoadElements { get; set; } = new List<MobileLoadElement>();
}
