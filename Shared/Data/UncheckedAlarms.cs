using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AliantWebS.Data
{
    /// <summary>
    /// Alarm thresholds the collection loop could not check. A reading whose calibration (C1/C0) cannot be read, or whose
    /// coefficient is not a number, is still stored, raw as every reading is; but a raw value is never compared with a
    /// threshold typed in calibrated units (Rafic, 2026-10-22). While that lasts it is said on every page (MainLayout)
    /// and on Alarm Management, and ReportsService journals it once when it starts and once when it ends.
    /// </summary>
    public sealed class UncheckedAlarms
    {
        /// <summary>What is not checked and why, and since when (local time).</summary>
        public sealed record State(string Message, DateTime Since);

        public enum Change { None, Started, Updated, Ended }

        // Channels named per reason in the message; the count says how many there are in all.
        private const int NamedPerReason = 5;

        private State? current;

        /// <summary>Null while every reading that has a threshold is checked.</summary>
        public State? Current => Volatile.Read(ref current);

        /// <summary>Raised on the collection loop's thread whenever <see cref="Current"/> changes.</summary>
        public event Action? Changed;

        /// <summary>After one batch of reports: the readings it could not check, as ("sensor / channel", why).</summary>
        public Change Update(IReadOnlyCollection<(string Reading, string Reason)> uncheckedReadings, DateTime now)
        {
            var before = Current;
            var after = uncheckedReadings.Count == 0 ? null : new State(Describe(uncheckedReadings), before?.Since ?? now);
            if (after?.Message == before?.Message) return Change.None;

            Volatile.Write(ref current, after);
            Changed?.Invoke();
            return before == null ? Change.Started : after == null ? Change.Ended : Change.Updated;
        }

        // Each reason once, with how many channels it stops and the first few of them.
        private static string Describe(IEnumerable<(string Reading, string Reason)> uncheckedReadings) =>
            string.Join(" ; ", uncheckedReadings
                .GroupBy(u => u.Reason)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    var readings = g.Select(u => u.Reading).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
                    var named = string.Join(", ", readings.Take(NamedPerReason)) + (readings.Count > NamedPerReason ? "…" : "");
                    return $"{g.Key} ({readings.Count} voie(s) : {named})";
                }));
    }
}
