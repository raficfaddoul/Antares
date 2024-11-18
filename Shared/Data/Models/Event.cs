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
    public virtual ICollection<Axe> Axes { get; } = new List<Axe>();

    [InverseProperty("Event")]
    public virtual ICollection<ChargesScenique> ChargesSceniques { get; } = new List<ChargesScenique>();

    [InverseProperty("Event")]
    public virtual ICollection<Charpente> Charpentes { get; } = new List<Charpente>();

    [InverseProperty("Event")]
    public virtual ICollection<Equipement> Equipements { get; } = new List<Equipement>();

    [InverseProperty("Event")]
    public virtual ICollection<EventLoadCase> EventLoadCases { get; } = new List<EventLoadCase>();

    [InverseProperty("Event")]
    public virtual ICollection<EventMpscenario> EventMpscenarios { get; } = new List<EventMpscenario>();

    [ForeignKey("StructureId")]
    [InverseProperty("Events")]
    public virtual Structure Structure { get; set; } = null!;
}
