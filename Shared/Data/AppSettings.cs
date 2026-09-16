using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Operational settings stored in the AppSetting key/value table, so they can be changed without
    /// a rebuild. The table is created idempotently at startup (Program.EnsureDatabaseSchema).
    ///
    /// Every getter takes the compiled-in value as its default, so the application behaves exactly as
    /// before until a row is written — and keeps working if the database is unreachable.
    ///
    /// The whole table is tiny and read often (the alarm loop reads it every cycle), so it is cached
    /// until a write drops the cache. There is no TTL to guess at.
    /// </summary>
    public static class AppSettings
    {
        // Set once at startup (Startup.Configure). When null, every getter returns its default.
        public static IDbContextFactory<AliantWebContext> DbFactory { get; set; }

        // ── Keys ──────────────────────────────────────────────────────────────────────────────
        public const string SirenMuted        = "alarm.siren.muted";
        public const string EmailMuted        = "alarm.email.muted";
        public const string EmailIntervalMin  = "alarm.email.intervalMinutes";
        public const string AlarmApiBaseUrl   = "alarm.api.baseUrl";

        // Three alarm levels, expressed as a percentage of each sensor's own "Seuil Alarme"
        // threshold, plus which level actually drives the siren and the e-mails.
        public const string AlarmLevel1Pct    = "alarm.level1.percent";
        public const string AlarmLevel2Pct    = "alarm.level2.percent";
        public const string AlarmLevel3Pct    = "alarm.level3.percent";
        public const string AlarmSirenLevel   = "alarm.siren.level";
        public const string AlarmEmailLevel   = "alarm.email.level";

        // Collection of the sensors' reports paused from /alarm-management. NOT a mute: while it is true no
        // report is read at all, so no new alarm can be detected. Persisted so a paused development
        // container stays paused across restarts, and a production one never pauses by accident on its own.
        public const string ReportFetchPaused = "monitoring.fetch.paused";

        // Layout optimiser: palonniers along the beams and mothers across them only - no inclined
        // palonnier, as in every hand-written study. One value for everyone; false keeps inclined ones.
        public const string OptimizerOrthogonalOnly = "optimizer.orthogonalOnly";

        // The two settings below say what the optimiser optimises, and how hard it looks for it. Both
        // apply to EVERYONE - there is one layout engine and one answer, so neither is a per-user
        // preference - and only a DEVELOPER may change them (Settings page, section « Outils développeur »).

        // The ranking a layout is kept by: the criterion KEYS in order, comma separated, first criterion
        // first - "palonniers,masse,pieces,longueur,marge". The keys are the stored contract and never
        // change: a label on the Settings page can be reworded, a key cannot, or a row written last month
        // would read as a different ranking. An empty or unreadable value means the ranking compiled into
        // Objective.cs, and the application says which one is in force rather than guessing.
        public const string OptimizerObjectiveOrder = "optimizer.objective.order";

        // How hard the optimiser looks: "bounds" (the default) places greedily and quotes, beside the
        // result, the bound nobody can beat; "exact" examines the possible groupings and keeps the best by
        // the ranking above, saying whether the optimum is proven or the search was stopped by its budget.
        // Any other value is not a search level and reads as "bounds", which is the behaviour in force today.
        public const string OptimizerSearchLevel = "optimizer.searchLevel";

        // Set once the stored scenic loads were moved to the grid's axes (Program.EnsureDatabaseSchema).
        public const string CoordinateAxes    = "coords.axes";

        private static readonly object _lock = new();
        private static Dictionary<string, string> _cache;

        // Bumped by every Invalidate. A read that started before a write finishes after it with the old values:
        // it may return them, but must not cache them, or they would stay until the next write.
        private static long _generation;

        public static void Invalidate()
        {
            lock (_lock) { _cache = null; _generation++; }
        }

        private static async Task<Dictionary<string, string>> LoadAsync()
        {
            long generation;
            lock (_lock)
            {
                if (_cache != null) return _cache;
                generation = _generation;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (DbFactory == null) return map;

            try
            {
                using var db = await DbFactory.CreateDbContextAsync();
                var rows = await db.Database
                    .SqlQueryRaw<SettingRow>("SELECT [Key], [Value] FROM AppSetting")
                    .ToListAsync();

                foreach (var r in rows)
                    if (!string.IsNullOrWhiteSpace(r.Key)) map[r.Key] = r.Value;
            }
            catch (Exception ex)
            {
                // Never let a settings read break a caller — fall back to the compiled defaults.
                Console.WriteLine($"[AppSettings] read failed, using defaults: {ex.Message}");
                return map;
            }

            lock (_lock)
            {
                if (_generation == generation) _cache = map;
            }
            return map;
        }

        private class SettingRow
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }

        public static async Task<string> GetStringAsync(string key, string fallback)
        {
            var map = await LoadAsync();
            return map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
        }

        public static async Task<double> GetDoubleAsync(string key, double fallback)
        {
            var raw = await GetStringAsync(key, null);
            if (raw == null) return fallback;
            if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            if (double.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        public static async Task<int> GetIntAsync(string key, int fallback)
        {
            var raw = await GetStringAsync(key, null);
            return raw != null && int.TryParse(raw.Trim(), out var v) ? v : fallback;
        }

        public static async Task<bool> GetBoolAsync(string key, bool fallback)
        {
            var raw = await GetStringAsync(key, null);
            if (raw == null) return fallback;
            raw = raw.Trim();
            if (bool.TryParse(raw, out var b)) return b;
            return raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Upserts a setting and drops the cache so the new value is read immediately.</summary>
        public static async Task SetAsync(string key, string value, string description = null)
        {
            if (DbFactory == null) return;

            try
            {
                using var db = await DbFactory.CreateDbContextAsync();
                await db.Database.ExecuteSqlRawAsync(@"
MERGE AppSetting AS t
USING (SELECT {0} AS [Key]) AS s ON t.[Key] = s.[Key]
WHEN MATCHED THEN UPDATE SET [Value] = {1}, Description = COALESCE({2}, Description), UpdatedAt = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT ([Key], [Value], Description, UpdatedAt) VALUES ({0}, {1}, {2}, SYSDATETIME());",
                    key, value ?? "", (object)description ?? DBNull.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppSettings] write failed for '{key}': {ex.Message}");
            }
            finally
            {
                Invalidate();
            }
        }

        public static Task SetAsync(string key, bool value, string description = null)
            => SetAsync(key, value ? "true" : "false", description);

        public static Task SetAsync(string key, double value, string description = null)
            => SetAsync(key, value.ToString(CultureInfo.InvariantCulture), description);

        public static Task SetAsync(string key, int value, string description = null)
            => SetAsync(key, value.ToString(CultureInfo.InvariantCulture), description);

        /// <summary>All settings currently stored, for an editor UI.</summary>
        public static async Task<Dictionary<string, string>> GetAllAsync()
            => new Dictionary<string, string>(await LoadAsync(), StringComparer.OrdinalIgnoreCase);
    }
}
