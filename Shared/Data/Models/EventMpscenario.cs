using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("EventMPScenario")]
public partial class EventMpscenario
{
    [Key]
    [Column("EventMPScenarioID")]
    public Guid EventMpscenarioId { get; set; }

    [Column("EventID")]
    public Guid EventId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Comments { get; set; }

    [ForeignKey("EventId")]
    [InverseProperty("EventMpscenarios")]
    public virtual Event Event { get; set; } = null!;

    [InverseProperty("EventMpscenario")]
    public virtual ICollection<MotherGridPalonnier> MotherGridPalonniers { get; } = new List<MotherGridPalonnier>();
}
