using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Static journal writer for code that has no DI scope and no signed-in user: the background
    /// monitoring loop. Rows written here have no UserName — the journal page shows them as
    /// "Système".
    ///
    /// Same pattern as AppSettings/SensorCalibration: DbFactory is set once at startup.
    /// User-initiated actions must go through the scoped JournalService instead, so they carry
    /// the identity of whoever performed them.
    /// </summary>
    public static class JournalWriter
    {
        public static IDbContextFactory<AliantWebContext> DbFactory { get; set; }

        public static async Task WriteSystemAsync(string category, string action, string target = null,
                                                  string details = null, byte severity = 0,
                                                  Guid? structureId = null, Guid? eventId = null)
        {
            if (DbFactory == null) return;

            try
            {
                using var db = await DbFactory.CreateDbContextAsync();

                db.Journal.Add(new JournalEntry
                {
                    Timestamp   = DateTime.Now,
                    UserName    = null,          // system
                    UserEmail   = null,
                    Category    = Cut(category, 50),
                    Action      = Cut(action, 100),
                    Target      = Cut(target, 400),
                    Details     = details,
                    Severity    = severity,
                    StructureId = structureId,
                    EventId     = eventId
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Never let the journal break the monitoring loop.
                Console.WriteLine($"[Journal] system write failed ({category}/{action}): {ex.Message}");
            }
        }

        private static string Cut(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
