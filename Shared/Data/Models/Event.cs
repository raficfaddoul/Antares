using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Event")]
public partial class Event
{
    [Key]
    [Column("EventID")]
    public Guid EventId { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? Description { get; set; }

    public double? TauxMoyen { get; set; }

    public double? Point0X { get; set; }

    public double? Point0Y { get; set; }

    [InverseProperty("Event")]
    public virtual ICollection<Axe> Axes { get; set; } = new List<Axe>();

    [InverseProperty("Event")]
    public virtual ICollection<Equipement> Equipements { get; set; } = new List<Equipement>();

    [InverseProperty("Event")]
    public virtual ICollection<EventLoadCase> EventLoadCases { get; set; } = new List<EventLoadCase>();

    [InverseProperty("Event")]
    public virtual ICollection<EventMpscenario> EventMpscenarios { get; set; } = new List<EventMpscenario>();

    [ForeignKey("StructureId")]
    [InverseProperty("Events")]
    public virtual Structure Structure { get; set; } = null!;
}
