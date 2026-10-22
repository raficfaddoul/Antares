using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AliantWebS.Data
{
    /// <summary>
    /// Every sensor channel's calibration y = C1·x + C0, as one load of the C1/C0 attributes found it (SensorCalibration
    /// loads and caches it). Asking it for a channel gives that channel's coefficients or throws: it never gives raw
    /// values for calibrated ones (Rafic, 2026-10-22).
    /// <list type="bullet">
    /// <item>A channel with no coefficient set (no attribute, or a blank one) is identity, C1 = 1 and C0 = 0: not
    /// calibrated, a safe no-op until real values are set.</item>
    /// <item>A coefficient that is set but is not a finite number makes its channel unusable: asking for that channel
    /// throws <see cref="CalibrationReadException"/> naming the sensor, the attribute and the value. The other channels
    /// are unaffected.</item>
    /// <item>The table of a load that failed (<see cref="Unreadable"/>) throws that failure for every channel.</item>
    /// </list>
    /// </summary>
    public sealed class CalibrationTable
    {
        /// <summary>One C1/C0 attribute of a sensor, as stored: Value is the text typed in the settings.</summary>
        public sealed record Attribute(Guid SensorId, string? SensorName, string? Name, string? Value);

        /// <summary>No coefficient anywhere: what a database without calibration gives.</summary>
        public static readonly CalibrationTable Identity = new(new(), new(), null);

        // Keyed by (sensor id, channel id) lower-cased: the Influx tags' form (Guid.ToString()), whatever the caller's case.
        private readonly Dictionary<(string SensorId, string ChannelId), (double C1, double C0)> coefficients;   // non-identity only
        private readonly Dictionary<(string SensorId, string ChannelId), string> unusable;                       // why, per channel
        private readonly CalibrationReadException? unreadable;

        private CalibrationTable(
            Dictionary<(string, string), (double, double)> coefficients,
            Dictionary<(string, string), string> unusable,
            CalibrationReadException? unreadable)
        {
            this.coefficients = coefficients;
            this.unusable = unusable;
            this.unreadable = unreadable;
        }

        /// <summary>The table of a load that failed: every channel asked of it throws that failure.</summary>
        public static CalibrationTable Unreadable(CalibrationReadException failure) =>
            new(new(), new(), failure ?? throw new ArgumentNullException(nameof(failure)));

        /// <summary>Why each unusable channel cannot be calibrated, one sentence per channel.</summary>
        public IReadOnlyCollection<string> UnusableChannels => unusable.Values;

        /// <summary>
        /// The calibrated (non-identity) channels among those asked for, keyed (sensor id, channel id) lower-cased as the
        /// Influx tags are. Throws <see cref="CalibrationReadException"/> when the table could not be read, or when a
        /// channel asked for is unusable (every such channel named).
        /// </summary>
        public List<KeyValuePair<(string sensorId, string channelId), (double C1, double C0)>> Of(
            IEnumerable<(string SensorId, string ChannelId)> channels)
        {
            if (unreadable != null)
                throw new CalibrationReadException(unreadable.Message, unreadable.InnerException);

            var keys = channels.Select(c => Key(c.SensorId, c.ChannelId)).Distinct().ToList();

            var reasons = keys.Where(unusable.ContainsKey).Select(k => unusable[k]).OrderBy(r => r, StringComparer.Ordinal).ToList();
            if (reasons.Count > 0)
                throw new CalibrationReadException($"Étalonnage inutilisable — {string.Join(" ; ", reasons)}");

            return keys.Where(coefficients.ContainsKey)
                .Select(k => new KeyValuePair<(string sensorId, string channelId), (double C1, double C0)>(k, coefficients[k]))
                .ToList();
        }

        /// <summary>One channel's (C1, C0): identity when not calibrated; throws as <see cref="Of"/> does.</summary>
        public (double C1, double C0) For(string sensorId, string channelId)
        {
            var found = Of(new[] { (sensorId, channelId) });
            return found.Count == 0 ? (1.0, 0.0) : found[0].Value;
        }

        /// <summary>
        /// The table the sensors' C1/C0 attributes give. An attribute belongs to a channel when its name is
        /// "&lt;accepted name&gt;_&lt;channel name&gt;", any case (e.g. C1_weight, or an alias mapped to C1 in TagMappingTable).
        /// </summary>
        public static CalibrationTable FromAttributes(
            IEnumerable<Attribute> attributes,
            IEnumerable<(Guid Id, string Name)> channels,
            IReadOnlySet<string> c1Names,
            IReadOnlySet<string> c0Names)
        {
            var coefficients = new Dictionary<(string, string), (double, double)>();
            var unusable = new Dictionary<(string, string), string>();
            var namedChannels = channels.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();

            foreach (var bySensor in attributes.GroupBy(a => a.SensorId))
            {
                foreach (var channel in namedChannels)
                {
                    var c1 = Coefficient(bySensor, c1Names, channel.Name, notSet: 1.0);
                    var c0 = Coefficient(bySensor, c0Names, channel.Name, notSet: 0.0);
                    var key = Key(bySensor.Key.ToString(), channel.Id.ToString());

                    var problems = new[] { c1.Problem, c0.Problem }.Where(p => p != null).ToList();
                    if (problems.Count > 0)
                    {
                        unusable[key] = $"{bySensor.First().SensorName} / {channel.Name} : {string.Join(", ", problems)}";
                        continue;
                    }

                    if (c1.Value == 1.0 && c0.Value == 0.0) continue;   // identity → nothing to apply

                    coefficients[key] = (c1.Value, c0.Value);
                }
            }

            return new CalibrationTable(coefficients, unusable, null);
        }

        // The coefficient a sensor's attributes give one channel: notSet when none is set, or why the one set is unusable.
        private static (double Value, string? Problem) Coefficient(
            IEnumerable<Attribute> sensorAttributes, IReadOnlySet<string> names, string channel, double notSet)
        {
            var attribute = sensorAttributes.FirstOrDefault(a => a.Name != null && names.Any(n =>
                string.Equals(a.Name.Trim(), $"{n}_{channel}", StringComparison.OrdinalIgnoreCase)));

            if (attribute == null || string.IsNullOrWhiteSpace(attribute.Value)) return (notSet, null);

            return TryParseCoefficient(attribute.Value, out var value)
                ? (value, null)
                : (notSet, $"{attribute.Name?.Trim()} = « {attribute.Value.Trim()} » n'est pas un nombre");
        }

        /// <summary>
        /// A coefficient as typed by hand: "2.5", or "2,5" since the app runs under a French locale. Only a finite
        /// number is one: "NaN", "∞" and values beyond a double's range are refused like any other text.
        /// </summary>
        public static bool TryParseCoefficient(string raw, out double value)
        {
            raw = raw.Trim();
            bool parsed = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                       || double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && double.IsFinite(value);
        }

        private static (string, string) Key(string sensorId, string channelId) =>
            (sensorId.Trim().ToLowerInvariant(), channelId.Trim().ToLowerInvariant());
    }
}
