using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>Categories used by the journal filter. Kept as constants so a typo can't split a group.</summary>
    public static class JournalCategory
    {
        public const string Alarme        = "Alarme";
        public const string Edition       = "Édition";
        public const string Configuration = "Configuration";
        public const string Calibration   = "Calibration";
        public const string Donnees       = "Données";
        public const string Session       = "Session";
    }

    /// <summary>
    /// Records deliberate user actions in the Journal table: who did what, when, to which target.
    ///
    /// Scoped, so it lives for one Blazor circuit and resolves the signed-in user once. Writes are
    /// explicit — every call site is a place where somebody decided something (reset an alarm,
    /// change a threshold, export a file). The background monitoring loop is deliberately NOT
    /// instrumented: it has no user, and its activity already goes to the container log.
    ///
    /// A journal write must never break the action it records, so every failure is swallowed and
    /// logged. Losing an audit row is bad; refusing to reset an alarm because the audit row failed
    /// would be worse.
    /// </summary>
    public class JournalService
    {
        private readonly IDbContextFactory<AliantWebContext> _dbFactory;
        private readonly AuthenticationStateProvider _authStateProvider;

        private bool _userResolved;
        private string _userName;
        private string _userEmail;

        public JournalService(IDbContextFactory<AliantWebContext> dbFactory,
                              AuthenticationStateProvider authStateProvider)
        {
            _dbFactory = dbFactory;
            _authStateProvider = authStateProvider;
        }

        /// <summary>Journals an action performed by the currently signed-in user.</summary>
        public async Task WriteAsync(string category, string action, string target = null,
                                     string details = null, byte severity = 0,
                                     Guid? structureId = null, Guid? eventId = null)
        {
            var (name, email) = await ResolveUserAsync();
            await InsertAsync(name, email, category, action, target, details, severity, structureId, eventId);
        }

        /// <summary>
        /// Journals an action for an explicitly named user. Needed outside a Blazor circuit — the
        /// Identity razor pages (login / logout), where the authentication state provider has no
        /// state to give.
        /// </summary>
        public Task WriteAsAsync(string userName, string userEmail, string category, string action,
                                 string target = null, string details = null, byte severity = 0,
                                 Guid? structureId = null, Guid? eventId = null)
            => InsertAsync(userName, userEmail, category, action, target, details, severity, structureId, eventId);

        private async Task InsertAsync(string userName, string userEmail, string category, string action,
                                       string target, string details, byte severity,
                                       Guid? structureId, Guid? eventId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();

                db.Journal.Add(new JournalEntry
                {
                    Timestamp   = DateTime.Now,
                    UserName    = Trim(userName, 256),
                    UserEmail   = Trim(userEmail, 256),
                    Category    = Trim(category, 50),
                    Action      = Trim(action, 100),
                    Target      = Trim(target, 400),
                    Details     = details,
                    Severity    = severity,
                    StructureId = structureId,
                    EventId     = eventId
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Journal] write failed ({category}/{action}): {ex.Message}");
            }
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private async Task<(string Name, string Email)> ResolveUserAsync()
        {
            if (_userResolved) return (_userName, _userEmail);
            _userResolved = true;

            try
            {
                // Throws outside a circuit (no authentication state has been set) — treat that as
                // "unknown user" rather than as a failure to journal.
                var state = await _authStateProvider.GetAuthenticationStateAsync();
                var principal = state?.User;
                if (principal?.Identity?.IsAuthenticated != true) return (null, null);

                _userName = principal.Identity.Name;
                _userEmail = principal.FindFirst(ClaimTypes.Email)?.Value;

                // The e-mail is usually not a claim; look it up once per circuit.
                if (string.IsNullOrWhiteSpace(_userEmail) && !string.IsNullOrWhiteSpace(_userName))
                {
                    using var db = await _dbFactory.CreateDbContextAsync();
                    _userEmail = await db.AspNetUsers
                        .Where(u => u.UserName == _userName)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Journal] could not resolve the current user: {ex.Message}");
            }

            return (_userName, _userEmail);
        }
    }
}
