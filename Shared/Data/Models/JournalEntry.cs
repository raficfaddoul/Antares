using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AliantWebS.Models;

/// <summary>
/// One recorded user action. The table is created idempotently at startup
/// (Program.EnsureDatabaseSchema), like AlarmState and AppSetting.
///
/// Only deliberate actions are journaled — the monitoring loop is NOT instrumented, so every row
/// has a user behind it. UserName/UserEmail are still nullable: identity can be unavailable (a
/// request outside a Blazor circuit), and losing the row would be worse than losing the name.
/// </summary>
[Table("Journal")]
public partial class JournalEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long JournalId { get; set; }

    public DateTime Timestamp { get; set; }

    [StringLength(256)]
    public string? UserName { get; set; }

    [StringLength(256)]
    public string? UserEmail { get; set; }

    /// <summary>Broad grouping shown as a column and used by the filter — see JournalCategory.</summary>
    [StringLength(50)]
    public string? Category { get; set; }

    /// <summary>Stable code tag (alarm.reset, siren.mute, …), never a translated sentence.</summary>
    [StringLength(100)]
    public string? Action { get; set; }

    /// <summary>What the action was applied to: sensor, element, setting key, file name.</summary>
    [StringLength(400)]
    public string? Target { get; set; }

    /// <summary>Free text or a small JSON payload, e.g. {"from":1200,"to":1500}.</summary>
    public string? Details { get; set; }

    public Guid? StructureId { get; set; }

    public Guid? EventId { get; set; }

    /// <summary>0 = info, 1 = warning, 2 = alarm.</summary>
    public byte Severity { get; set; }
}
