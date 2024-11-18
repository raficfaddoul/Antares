using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("LoadCase", Schema = "adbo")]
public partial class LoadCase
{
    [Key]
    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    [Column("StructureID")]
    public Guid? StructureId { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [InverseProperty("LoadCase")]
    public virtual ICollection<DynamicLoad> DynamicLoads { get; } = new List<DynamicLoad>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<DynamicPointLoad> DynamicPointLoads { get; } = new List<DynamicPointLoad>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<EventLoadCase> EventLoadCases { get; } = new List<EventLoadCase>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<MobileLoad> MobileLoads { get; } = new List<MobileLoad>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<StaticLoad> StaticLoads { get; } = new List<StaticLoad>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<StaticPointLoad> StaticPointLoads { get; } = new List<StaticPointLoad>();
}
