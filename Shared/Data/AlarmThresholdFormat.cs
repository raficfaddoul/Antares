using System;
using System.Collections.Generic;
using System.Globalization;

namespace AliantWebS.Data
{
    /// <summary>
    /// The "Seuil Alarme" attribute holds one threshold per channel, packed as "weight:2000;temperature:60".
    ///
    /// A part that is not "channel:number" is unreadable, and every reader skips it: a bare "1500" is no
    /// threshold at all, for any channel. So is a channel given two different values (Rafic, 2026-10-22): which
    /// one is meant cannot be known. Unreadable parts are handed back so a page or the log can say so,
    /// rather than a sensor quietly dropping out of the gauges and the alarms.
    /// </summary>
    public static class AlarmThresholdFormat
    {
        /// <summary>Example of the expected form, for messages.</summary>
        public const string Example = "weight:1500;temperature:60";

        /// <summary>
        /// Threshold per channel name (case-insensitive; the same value given twice counts once, two different values
        /// give the channel none and make every part naming it unreadable). A zero threshold is returned as read:
        /// whether it counts is the caller's business. Each unreadable part, trimmed, is added to
        /// <paramref name="unreadable"/> when one is given.
        /// </summary>
        public static Dictionary<string, double> Parse(string value, ICollection<string> unreadable = null)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value)) return result;

            var partOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // the part that gave it
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                // "weight:1500;" and "weight:1500; " end with an empty part, which is no mistake.
                if (string.IsNullOrWhiteSpace(part)) continue;

                var kv = part.Split(':');
                if (kv.Length != 2
                    || string.IsNullOrWhiteSpace(kv[0])
                    || !double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
                {
                    unreadable?.Add(part.Trim());
                    continue;
                }

                var channel = kv[0].Trim();
                if (ambiguous.Contains(channel))
                {
                    unreadable?.Add(part.Trim());
                }
                else if (!result.TryGetValue(channel, out var earlier))
                {
                    result[channel] = threshold;
                    partOf[channel] = part.Trim();
                }
                else if (earlier != threshold)
                {
                    result.Remove(channel);
                    ambiguous.Add(channel);
                    unreadable?.Add(partOf[channel]);
                    unreadable?.Add(part.Trim());
                }
            }

            return result;
        }
    }
}
