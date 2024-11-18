using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("EventLoadCase")]
public partial class EventLoadCase
{
    [Key]
    [Column("EventLoadCaseID")]
    public Guid EventLoadCaseId { get; set; }

    [Column("EventID")]
    public Guid EventId { get; set; }

    [Column("LoadCaseID")]
    public Guid LoadCaseId { get; set; }

    [ForeignKey("EventId")]
    [InverseProperty("EventLoadCases")]
    public virtual Event Event { get; set; } = null!;

    [ForeignKey("LoadCaseId")]
    [InverseProperty("EventLoadCases")]
    public virtual LoadCase LoadCase { get; set; } = null!;
}
