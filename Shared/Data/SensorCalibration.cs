using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Per-sensor, per-channel linear calibration  y = C1·x + C0  turning raw InfluxDB samples into
    /// reported values everywhere (plots, gauges, charge ratios, CSV export). C1/C0 live as
    /// AttributeElements on the sensor Element, one pair per channel, named
    /// "&lt;C1prefix&gt;_&lt;channel&gt;" / "&lt;C0prefix&gt;_&lt;channel&gt;" (e.g. C1_weight, C0_weight).
    ///
    /// The "C1"/"C0" prefixes are hardcoded CODE tags resolved to the user's names through the
    /// TagMappingTable (default identity → literally C1_weight, C0_weight, …).
    ///
    /// Missing coefficients default to identity (C1 = 1, C0 = 0): a sensor with no calibration
    /// attributes passes through unchanged — a safe no-op until real values are set.
    ///
    /// A load that fails, or a coefficient that is not a number, is never turned into raw values that
    /// look calibrated (Rafic, 2026-10-22): whoever asks for those values gets a CalibrationReadException
    /// (see CalibrationTable). The pages show it as unreadable data, the export is refused, and the alarm
    /// checks skip those readings (UncheckedAlarms).
    ///
    /// The transform is applied INSIDE the Flux query (a map() before aggregateWindow) so that
    /// max / downsampling aggregate the CALIBRATED values — correct even when the slope C1 is
    /// negative (where max(raw) would otherwise become min after a naive post-multiply).
    /// </summary>
    public static class SensorCalibration
    {
        // Set once at startup (Startup.Configure). When null, calibration is a no-op.
        public static IDbContextFactory<AliantWebContext> DbFactory { get; set; }

        private const string C1Code = "C1";
        private const string C0Code = "C0";

        /// <summary>Where the calibration map() has to run relative to aggregateWindow.</summary>
        public enum CalibrationMode
        {
            None,      // nothing calibrated in this query
            PreMap,    // map before aggregation (only safe placement for this aggregation)
            PostMap,   // map after aggregation — cheap: runs on aggregated rows only
            Split      // split the stream, flip the aggregation for negative slopes, then map
        }

        /// <summary>
        /// One disjoint slice of the data with the aggregation function that slice needs. Filters are
        /// plain OR-of-equality over sensor/_field so Influx pushes them into the storage read.
        /// </summary>
        public class SplitBranch
        {
            public string Filter { get; set; } = "";
            public string Fn { get; set; } = "";
        }

        /// <summary>
        /// How to splice the calibration into a Flux query. See BuildPlanAsync for the reasoning.
        /// </summary>
        public class CalibrationPlan
        {
            public CalibrationMode Mode { get; set; } = CalibrationMode.None;
            /// <summary>The <c>|&gt; map(...)</c> clause. Empty when Mode == None.</summary>
            public string MapClause { get; set; } = "";
            /// <summary>Disjoint read branches, each with its own aggregation. Split mode only.</summary>
            public List<SplitBranch> Branches { get; set; } = new();
        }

        /// <summary>
        /// Decides where y = C1·x + C0 can be applied for a given aggregation.
        ///
        /// The transform is linear, so it commutes with aggregation in most cases — and applying it
        /// AFTER aggregateWindow is far cheaper (a few hundred aggregated rows instead of every raw
        /// sample). The exception is order-based aggregations with a negative slope, because a
        /// decreasing transform reverses the ordering:
        ///
        ///   C1 &gt; 0 :  max(C1·x + C0) = C1·max(x) + C0
        ///   C1 &lt; 0 :  max(C1·x + C0) = C1·min(x) + C0     ← aggregate with min, then calibrate
        ///
        ///   mean    :  mean(C1·x + C0) = C1·mean(x) + C0    ← linear for ANY sign, never flips
        ///
        /// So: mean (and no-aggregation) never needs a flip; max/min with only positive slopes can
        /// map afterwards as-is; max/min with any negative slope splits the stream so those pairs
        /// aggregate with the opposite function. Anything else (sum, count, …) is not linear in the
        /// aggregate — those fall back to mapping before aggregation, which is always correct.
        ///
        /// Throws CalibrationReadException when the calibration cannot be read, or when a pair the
        /// query reads has an unusable coefficient.
        /// </summary>
        public static async Task<CalibrationPlan> BuildPlanAsync(
            IEnumerable<string> sensorIds, IEnumerable<string> channelIds, string aggregateFn)
            => BuildPlan(await LoadTableAsync(), sensorIds, channelIds, aggregateFn);

        /// <summary>BuildPlanAsync on a table already loaded.</summary>
        public static CalibrationPlan BuildPlan(
            CalibrationTable table, IEnumerable<string> sensorIds, IEnumerable<string> channelIds, string aggregateFn)
        {
            var plan = new CalibrationPlan();

            // Keep only the (sensor, channel) pairs this query actually asks for, so the map clause
            // stays small instead of listing every calibrated sensor in the database.
            var wantedSensors = new HashSet<string>(sensorIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var wantedChannels = new HashSet<string>(channelIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var coeffs = table.Of(from s in wantedSensors from c in wantedChannels select (s, c));
            if (coeffs.Count == 0) return plan;

            var sb = new StringBuilder();
            sb.Append("|> map(fn: (r) => ({ r with _value: ");
            foreach (var kv in coeffs)
            {
                var (sensorId, channelId) = kv.Key;
                var (c1, c0) = kv.Value;
                sb.Append($"if r.sensor == \"{sensorId}\" and r._field == \"{channelId}\" then {Flux(c1)} * r._value + {Flux(c0)} else ");
            }
            sb.Append("r._value }))");
            plan.MapClause = sb.ToString();

            string fn = (aggregateFn ?? "").Trim().ToLowerInvariant();

            // No aggregation in this query: placement is irrelevant, map the raw rows.
            if (string.IsNullOrEmpty(fn))
            {
                plan.Mode = CalibrationMode.PreMap;
                return plan;
            }

            // Linear in the aggregate and value-order independent → safe to map afterwards, any sign.
            if (fn == "mean" || fn == "first" || fn == "last" || fn == "median")
            {
                plan.Mode = CalibrationMode.PostMap;
                return plan;
            }

            if (fn == "max" || fn == "min")
            {
                // Pairs whose slope reverses the ordering.
                var negatives = coeffs.Where(kv => kv.Value.C1 < 0).ToList();
                if (negatives.Count == 0)
                {
                    plan.Mode = CalibrationMode.PostMap;   // ordering preserved → no flip needed
                    return plan;
                }

                // Partition by (channel, sign) rather than by sensor. A sensor may have C1 < 0 on
                // "weight" and be uncalibrated on "temperature"; splitting per channel puts it on the
                // flipped branch for one and the normal branch for the other, so mixed signs need no
                // slow fallback. Every branch is disjoint, so each series is still read once.
                string flipped = fn == "max" ? "min" : "max";
                string SensorsOr(IEnumerable<string> ids) =>
                    string.Join(" or ", ids.Select(s => $"r.sensor == \"{s}\""));

                var branches = new List<SplitBranch>();
                var plainChannels = new List<string>();

                foreach (var ch in wantedChannels)
                {
                    var negSensors = negatives
                        .Where(kv => string.Equals(kv.Key.channelId, ch, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key.sensorId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (negSensors.Count == 0)
                    {
                        plainChannels.Add(ch);   // no flip needed for this channel
                        continue;
                    }

                    branches.Add(new SplitBranch
                    {
                        Filter = $"({SensorsOr(negSensors)}) and (r._field == \"{ch}\")",
                        Fn = flipped
                    });

                    var posSensors = wantedSensors.Where(s => !negSensors.Contains(s)).ToList();
                    if (posSensors.Count > 0)
                    {
                        branches.Add(new SplitBranch
                        {
                            Filter = $"({SensorsOr(posSensors)}) and (r._field == \"{ch}\")",
                            Fn = fn
                        });
                    }
                }

                // All channels that need no flip read together in a single branch.
                if (plainChannels.Count > 0)
                {
                    string fieldsOr = string.Join(" or ", plainChannels.Select(c => $"r._field == \"{c}\""));
                    branches.Insert(0, new SplitBranch
                    {
                        Filter = $"({SensorsOr(wantedSensors)}) and ({fieldsOr})",
                        Fn = fn
                    });
                }

                plan.Mode = CalibrationMode.Split;
                plan.Branches = branches;
                return plan;
            }

            // sum / count / stddev / unknown: NOT equivalent after aggregation (e.g. sum adds C0
            // once per point). Only the expensive placement is correct.
            plan.Mode = CalibrationMode.PreMap;
            return plan;
        }

        /// <summary>
        /// Calibrate GetDataForExport results in place (raw / unaggregated data, so a post-multiply is
        /// exact for any slope). Inner keys are "sensorId|channelId" lower-cased.
        ///
        /// Throws CalibrationReadException, before any value changes, when the calibration cannot be
        /// read or a pair the export holds has an unusable coefficient: the export is refused rather
        /// than written raw (Rafic, 2026-10-22).
        /// </summary>
        public static async Task ApplyToExportAsync(Dictionary<DateTime, Dictionary<string, double>> data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Count == 0) return;

            ApplyToExport(await LoadTableAsync(), data);
        }

        /// <summary>ApplyToExportAsync on a table already loaded.</summary>
        public static void ApplyToExport(CalibrationTable table, Dictionary<DateTime, Dictionary<string, double>> data)
        {
            var pairKeys = data.Values.SelectMany(perTimestamp => perTimestamp.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Every pair checked first: Of throws for an unusable one before anything is changed.
            var byPair = table.Of(pairKeys.Select(SplitPairKey)).ToDictionary(
                kv => $"{kv.Key.sensorId}|{kv.Key.channelId}",
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);
            if (byPair.Count == 0) return;

            foreach (var perTimestamp in data.Values)
            {
                foreach (var pairKey in perTimestamp.Keys.ToList())
                {
                    if (byPair.TryGetValue(pairKey, out var c))
                        perTimestamp[pairKey] = c.C1 * perTimestamp[pairKey] + c.C0;
                }
            }
        }

        private static (string SensorId, string ChannelId) SplitPairKey(string pairKey)
        {
            int bar = pairKey.IndexOf('|');
            if (bar < 0)
                throw new ArgumentException($"Export key \"{pairKey}\" is not \"sensorId|channelId\".", nameof(pairKey));
            return (pairKey[..bar], pairKey[(bar + 1)..]);
        }

        // ---- cache -------------------------------------------------------------------------
        // The whole calibration table is tiny and changes only when someone edits a sensor's C1/C0
        // attribute. Loading it per GetData call cost ~1s each (≈6 calls per dashboard load), so it
        // is held until explicitly dropped — every code path that writes a coefficient calls
        // Invalidate(), so there is no TTL to guess at.
        //
        // A failed load is never cached: the next read tries again. A load that began before an
        // Invalidate() keeps its table to itself, since it may hold the coefficients from before the
        // edit (the generation tells). Loads are not single-flight: two concurrent reads may both
        // load, and each gets a whole table or the failure.
        private static readonly object _cacheLock = new();
        private static CalibrationTable? _cache;
        private static long _generation;

        public static void Invalidate()
        {
            lock (_cacheLock)
            {
                _cache = null;
                _generation++;
            }
        }

        /// <summary>
        /// The calibration of every sensor channel: cached, or loaded now. Identity when no database is
        /// configured (DbFactory null). When the load fails it is logged, and the table returned is
        /// CalibrationTable.Unreadable: every channel asked of it throws CalibrationReadException.
        /// </summary>
        public static async Task<CalibrationTable> LoadTableAsync()
        {
            var factory = DbFactory;
            if (factory == null) return CalibrationTable.Identity;

            long generation;
            lock (_cacheLock)
            {
                if (_cache != null)
                    return _cache;
                generation = _generation;
            }

            CalibrationTable table;
            try
            {
                table = await ReadTableAsync(factory);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SensorCalibration] load failed: {ex}");
                return CalibrationTable.Unreadable(new CalibrationReadException(
                    $"Les coefficients d'étalonnage (C1/C0) n'ont pas pu être lus : {ex.GetBaseException().Message}", ex));
            }

            lock (_cacheLock)
            {
                if (generation == _generation)
                    _cache = table;
            }

            return table;
        }

        // Every sensor's coefficients, keyed by (sensorId, channelId) in the same string form used by
        // the Influx tags (sensor.ElementId.ToString() and the channel id passed to GetData).
        // NOTE: loads ALL calibrated sensors/channels (not just the requested ones) so a single
        // cached table serves every query; callers ask it for their own pairs.
        private static async Task<CalibrationTable> ReadTableAsync(IDbContextFactory<AliantWebContext> factory)
        {
            using var db = await factory.CreateDbContextAsync();

            // Accepted C1/C0 attribute-name prefixes: the code tag itself PLUS every user alias, so
            // "C1_weight" keeps working whether or not a mapping row exists.
            var tagRows = await db.Set<TagMappingTable>()
                .Where(t => t.CodeTag == C1Code || t.CodeTag == C0Code)
                .AsNoTracking()
                .ToListAsync();
            var c1Prefixes = TagMappingResolver.AcceptedNames(tagRows, C1Code);
            var c0Prefixes = TagMappingResolver.AcceptedNames(tagRows, C0Code);

            // Every channel name, so "<prefix>_<channel>" can be matched back to a channel id.
            var channels = await db.Set<ChannelDataType>()
                .Select(c => new { c.ChannelDataTypeId, c.Name })
                .AsNoTracking()
                .ToListAsync();

            // Only the calibration attributes, as a flat projection — NOT every sensor with all of
            // its attribute collections (that Include-heavy query was the ~1s per-call cost).
            // Fetch anything that starts with an accepted prefix; the exact channel match happens
            // below. Underscores are escaped so they are literal, not LIKE wildcards.
            var likePatterns = c1Prefixes.Concat(c0Prefixes)
                .Select(p => p.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "[_]%")
                .Distinct()
                .ToList();

            var attrRows = await db.Set<AttributeElement>()
                .Where(ae => likePatterns.Any(p => EF.Functions.Like(ae.AttributeType.Name, p)))
                .Select(ae => new CalibrationTable.Attribute(ae.ElementId, ae.Element.Name, ae.AttributeType.Name, ae.Value))
                .AsNoTracking()
                .ToListAsync();

            var table = CalibrationTable.FromAttributes(
                attrRows, channels.Select(c => (c.ChannelDataTypeId, c.Name)), c1Prefixes, c0Prefixes);

            foreach (var reason in table.UnusableChannels)
                Console.Error.WriteLine($"[SensorCalibration] unusable coefficient, its channel's reads will fail: {reason}");

            return table;
        }

        // Format a coefficient as a Flux float literal (always with a decimal point, invariant, no
        // scientific notation) so Flux's typed arithmetic (float * float) doesn't choke on an int.
        private static string Flux(double d)
            => d.ToString("0.0##########", CultureInfo.InvariantCulture);
    }
}
