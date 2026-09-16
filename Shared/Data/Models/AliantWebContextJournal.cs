using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

/// <summary>
/// The journal DbSet lives in its own partial so a re-scaffold of AliantWebContext
/// (Scaffold-DbContext … -Force, see Program.cs) cannot wipe it.
/// </summary>
public partial class AliantWebContext
{
    public virtual DbSet<JournalEntry> Journal { get; set; }
}
