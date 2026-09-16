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
    public virtual ICollection<EventLoadCase> EventLoadCases { get; set; } = new List<EventLoadCase>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<StaticLoad> StaticLoads { get; set; } = new List<StaticLoad>();

    [InverseProperty("LoadCase")]
    public virtual ICollection<StaticPointLoad> StaticPointLoads { get; set; } = new List<StaticPointLoad>();
}
