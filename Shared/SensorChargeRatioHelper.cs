using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// Shared logic for the manille (shackle) charge-ratio colour map used by the SVG.
    /// Used by both DashBoardComponent2 and SurveillanceCapteursAntares so the Influx query and the
    /// ratio maths live in exactly one place.
    ///
    /// The ratio is measured against each sensor's OWN alarm threshold for that channel — the
    /// "Seuil Alarme" attribute, packed as "weight:2000;temperature:60". So 100% means "at the
    /// threshold" for that particular sensor, rather than against one global rated load. A sensor
    /// with no threshold for the channel gets no ratio at all: nothing is assumed for it.
    /// </summary>
    public static class SensorChargeRatioHelper
    {
        /// <summary>
        /// Per-sensor alarm threshold for one channel, keyed by ElementId. Sensors with no usable
        /// threshold are simply absent from the result. A threshold value with a part that is not
        /// "channel:number" (see AlarmThresholdFormat) is added to <paramref name="unreadable"/> when one is
        /// given, whatever channel was asked for.
        /// </summary>
        public static async Task<Dictionary<Guid, double>> LoadThresholdsAsync(
            IDbContextFactory<AliantWebContext> dbContextFactory,
            IReadOnlyCollection<Element> sensors,
            string channelName,
            ICollection<(Guid ElementId, string Value)> unreadable = null)
        {
            var result = new Dictionary<Guid, double>();
            if (dbContextFactory == null || sensors == null || sensors.Count == 0
                || string.IsNullOrWhiteSpace(channelName))
                return result;

            using var context = await dbContextFactory.CreateDbContextAsync();

            // Which attribute names carry a threshold — code tag plus every user alias.
            var accepted = await TagMappingResolver.AcceptedNamesAsync(context, "seuil_alarme");

            var sensorIds = sensors.Select(s => s.ElementId).ToList();
            var rows = await context.Set<AttributeElement>()
                .Where(ae => sensorIds.Contains(ae.ElementId))
                .Select(ae => new { ae.ElementId, Name = ae.AttributeType.Name, ae.Value })
                .AsNoTracking()
                .ToListAsync();

            foreach (var row in rows)
            {
                if (!TagMappingResolver.Matches(accepted, row.Name)) continue;
                if (string.IsNullOrWhiteSpace(row.Value)) continue;

                var unreadableParts = new List<string>();
                var thresholds = AlarmThresholdFormat.Parse(row.Value, unreadableParts);
                if (unreadableParts.Count > 0) unreadable?.Add((row.ElementId, row.Value));

                if (thresholds.TryGetValue(channelName.Trim(), out var t) && t != 0)
                    result[row.ElementId] = t;
            }

            return result;
        }

        /// <summary>
        /// Loads a structure's shackle (manille) sensors and their "weight" channel type.
        /// </summary>
        public static async Task<(List<Element> ShackleSensors, ChannelDataType WeightChannel)>
            LoadShackleSensorsAsync(IDbContextFactory<AliantWebContext> dbContextFactory, Structure structure)
        {
            if (dbContextFactory == null || structure == null)
                return (new List<Element>(), null);

            // Dedicated short-lived context — avoids "A second operation was started on this
            // context" collisions with the shared EntitiesService context used elsewhere.
            using var context = await dbContextFactory.CreateDbContextAsync();

            var weightScaleTypes = await context.Set<ElementType>()
                .Include(et => et.Category)
                .Include(et => et.ChannelDataTypeElementTypes)
                    .ThenInclude(cdtet => cdtet.ChannelDataType)
                .Where(et => et.Category != null
                          && et.Category.Name.ToLower() == "sensing device"
                          && et.Name.Contains("Manille"))
                .AsNoTracking()
                .ToListAsync();

            var weightChannel = weightScaleTypes
                .SelectMany(et => et.ChannelDataTypeElementTypes)
                .Select(cdtet => cdtet.ChannelDataType)
                .FirstOrDefault(cdt => cdt != null && cdt.Name == "weight");

            var typeIds = weightScaleTypes.Select(et => et.ElementTypeId).ToList();

            var sensors = await context.Set<Element>()
                .Where(e => e.StructureId == structure.StructureId
                         && typeIds.Contains(e.ElementTypeId ?? Guid.Empty))
                .AsNoTracking()
                .ToListAsync();

            return (sensors, weightChannel);
        }

        /// <summary>
        /// Per-sensor MAX weight over [startDate, endDate], keyed by ElementId (raw measured value,
        /// same units as the channel). Empty when there are no sensors / no weight channel / no data.
        /// </summary>
        public static async Task<Dictionary<Guid, double>> MaxWeightsAsync(
            IReadOnlyCollection<Element> shackleSensors,
            ChannelDataType weightChannelDataType,
            DateTime startDate,
            DateTime endDate,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var maxWeights = new Dictionary<Guid, double>();

            if (shackleSensors == null || shackleSensors.Count == 0 || weightChannelDataType == null)
                return maxWeights;

            var sensorIds = shackleSensors.Select(s => s.ElementId.ToString()).ToList();
            var channelIds = new List<string> { weightChannelDataType.ChannelDataTypeId.ToString() };

            try
            {
                // One aggregated max per sensor over the whole [startDate, endDate] window.
                var maxData = await InfluxService.GetData(
                    sensorIds, channelIds, startDate, endDate, ReportsService.BucketName,
                    aggregateWindowDuration: "36500d", aggregateFn: "max",
                    cancellationToken: cancellationToken);

                if (maxData == null || maxData.Count == 0) return maxWeights;

                var perSensorMax = maxData
                    .Where(r => r[2] != null)
                    .GroupBy(r => (string)r[1])
                    .ToDictionary(g => g.Key, g => g.Max(r => (double)r[2]));

                foreach (var kvp in perSensorMax)
                    if (Guid.TryParse(kvp.Key, out var sensorId))
                        maxWeights[sensorId] = kvp.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error computing sensor max weights: {ex.Message}");
            }

            return maxWeights;
        }

        /// <summary>
        /// Per-sensor charge ratio = (max value over [startDate, endDate]) / that sensor's own alarm
        /// threshold for the channel, keyed by ElementId. Sensors with no threshold are omitted —
        /// there is no default rated load to fall back on.
        /// </summary>
        public static async Task<Dictionary<Guid, double>> ComputeAsync(
            IDbContextFactory<AliantWebContext> dbContextFactory,
            IReadOnlyCollection<Element> shackleSensors,
            ChannelDataType weightChannelDataType,
            DateTime startDate,
            DateTime endDate,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var maxWeights = await MaxWeightsAsync(shackleSensors, weightChannelDataType, startDate, endDate, cancellationToken);
            if (maxWeights.Count == 0) return new Dictionary<Guid, double>();

            var thresholds = await LoadThresholdsAsync(dbContextFactory, shackleSensors, weightChannelDataType?.Name);

            var ratios = new Dictionary<Guid, double>();
            foreach (var kv in maxWeights)
                if (Ratio(thresholds, kv.Key, kv.Value) is double ratio)
                    ratios[kv.Key] = ratio;

            return ratios;
        }

        /// <summary>
        /// A reading rated against its own sensor's alarm threshold: value / threshold, or null for a sensor with no
        /// threshold, since nothing is assumed for it. The one rule behind the gauges, the plan's colours and the
        /// weekly peaks.
        /// </summary>
        public static double? Ratio(IReadOnlyDictionary<Guid, double> thresholds, Guid sensorId, double value)
            => thresholds.TryGetValue(sensorId, out var threshold) && threshold != 0 ? value / threshold : null;

        /// <summary>
        /// The dashboard's "Charge max manilles / seuil d'alarme" curve: for each week, Monday to Sunday and dated
        /// by its Monday, the highest reading rated against its own sensor's threshold, and the sensor that gave it.
        /// <paramref name="rows"/> are InfluxService.GetData rows: [0] time, [1] sensor id, [2] value. A reading
        /// with no value, or of a sensor with no threshold, is left out; a week with nothing left has no peak. On a
        /// tie the first reading keeps the peak.
        /// </summary>
        public static List<WeeklyPeak> WeeklyPeaks(IEnumerable<object[]> rows, IReadOnlyDictionary<Guid, double> thresholds)
        {
            var peaks = new SortedDictionary<DateTime, WeeklyPeak>();
            foreach (var r in rows)
            {
                if (r[0] is not DateTime at || r[2] == null) continue;
                if (!Guid.TryParse(r[1] as string, out var sensorId)) continue;
                if (Ratio(thresholds, sensorId, (double)r[2]) is not double ratio) continue;

                var week = InfluxService.WeekStart(at);
                if (!peaks.TryGetValue(week, out var peak) || ratio > peak.Ratio)
                    peaks[week] = new WeeklyPeak(week, ratio, sensorId);
            }
            return peaks.Values.ToList();
        }
    }

    /// <summary>One week's peak charge ratio and the sensor that reached it, see SensorChargeRatioHelper.WeeklyPeaks.</summary>
    public record WeeklyPeak(DateTime Week, double Ratio, Guid SensorId);
}
