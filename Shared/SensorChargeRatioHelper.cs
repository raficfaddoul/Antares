using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// The manille (shackle) charge ratios, so the Influx queries and the ratio maths live in exactly one place. Two
    /// responsibilities: the plan's charge-ratio colour map (<see cref="ComputeAsync"/>, the Surveillance tab's
    /// SurveillanceCapteursAntares) and the dashboard's shackle figures (DashBoardComponent2: the gauges with the
    /// critical-share rule <see cref="CriticalRatio"/> and <see cref="Percent"/>, see <see cref="Charge"/>). Both rest on
    /// the same thresholds (<see cref="ThresholdsByChannel"/>, the one parse loop) and <see cref="Ratio"/>, as do the
    /// weekly peaks (<see cref="WeeklyPeaks"/>) and each sensor's highest ratio (<see cref="MaxRatioBySensor"/>). The
    /// dashboard's part for every sensor type with an alarm threshold, beyond the shackles, lives in AlarmRatioCurves.
    ///
    /// The ratio is measured against each sensor's OWN alarm threshold for that channel — the
    /// "Seuil Alarme" attribute, packed as "weight:2000;temperature:60". So 100% means "at the
    /// threshold" for that particular sensor, rather than against one global rated load. A sensor
    /// with no threshold for the channel gets no ratio at all: nothing is assumed for it.
    /// </summary>
    public static class SensorChargeRatioHelper
    {
        /// <summary>A shackle (manille) is a sensing device whose type name contains this: the one definition.</summary>
        public const string ShackleTypeNamePart = "Manille";

        /// <summary>A shackle whose charge ratio is above this (not at it) counts as a critical element.</summary>
        public const double CriticalRatio = 0.80;

        // One aggregate window longer than any period asked for, so one row per sensor.
        private const string WholePeriodWindow = "36500d";

        /// <summary>
        /// Per-sensor alarm threshold for one channel, keyed by ElementId. Sensors with no usable
        /// threshold are simply absent from the result. A threshold value with a part that is not
        /// "channel:number" (see AlarmThresholdFormat) is added to <paramref name="unreadable"/> when one is
        /// given, whatever channel was asked for. The projection of <see cref="LoadThresholdsByChannelAsync"/> on one
        /// channel (<see cref="ForChannel"/>); empty, with no SQL, for no factory, no sensor or no channel name.
        /// </summary>
        public static async Task<Dictionary<Guid, double>> LoadThresholdsAsync(
            IDbContextFactory<AliantWebContext> dbContextFactory,
            IReadOnlyCollection<Element> sensors,
            string channelName,
            ICollection<(Guid ElementId, string Value)>? unreadable = null)
        {
            if (dbContextFactory == null || sensors == null || sensors.Count == 0
                || string.IsNullOrWhiteSpace(channelName))
                return new Dictionary<Guid, double>();

            var byChannel = await LoadThresholdsByChannelAsync(dbContextFactory, sensors, unreadable, CancellationToken.None);
            return ForChannel(byChannel, channelName);
        }

        /// <summary>
        /// Every channel's alarm threshold per sensor, as <see cref="ThresholdsByChannel"/> reads them from the sensors'
        /// "Seuil Alarme" attributes (the code tag seuil_alarme or any of its aliases). No sensor: an empty map, with no
        /// SQL. Throws on a null factory or null sensors: an empty map would pass for "no threshold".
        /// </summary>
        public static async Task<Dictionary<Guid, Dictionary<string, double>>> LoadThresholdsByChannelAsync(
            IDbContextFactory<AliantWebContext> dbContextFactory,
            IReadOnlyCollection<Element> sensors,
            ICollection<(Guid ElementId, string Value)>? unreadable,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dbContextFactory);
            ArgumentNullException.ThrowIfNull(sensors);
            if (sensors.Count == 0) return new Dictionary<Guid, Dictionary<string, double>>();

            using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Which attribute names carry a threshold — code tag plus every user alias.
            var accepted = await TagMappingResolver.AcceptedNamesAsync(context, "seuil_alarme");

            var sensorIds = sensors.Select(s => s.ElementId).ToList();
            var rows = await context.Set<AttributeElement>()
                .Where(ae => sensorIds.Contains(ae.ElementId))
                .Select(ae => new { ae.ElementId, Name = ae.AttributeType.Name, ae.Value })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Matches takes a null name as no match.
            return ThresholdsByChannel(
                rows.Where(row => TagMappingResolver.Matches(accepted, row.Name!)).Select(row => (row.ElementId, row.Value)),
                unreadable);
        }

        /// <summary>
        /// The alarm thresholds of every channel per sensor, from its "Seuil Alarme" values in row order: sensor →
        /// channel name (any case) → threshold. Per sensor and channel a later row's non-zero value wins; a zero is no
        /// threshold and replaces nothing; a blank value is no threshold and nothing to report. A row with a part that is
        /// not "channel:number" is added once to <paramref name="unreadable"/> when one is given, and its readable parts
        /// still count. A sensor with no threshold is absent.
        /// </summary>
        public static Dictionary<Guid, Dictionary<string, double>> ThresholdsByChannel(
            IEnumerable<(Guid ElementId, string? Value)> thresholdValues,
            ICollection<(Guid ElementId, string Value)>? unreadable = null)
        {
            ArgumentNullException.ThrowIfNull(thresholdValues);

            var result = new Dictionary<Guid, Dictionary<string, double>>();
            foreach (var (sensorId, value) in thresholdValues)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                var unreadableParts = new List<string>();
                var thresholds = AlarmThresholdFormat.Parse(value, unreadableParts);
                if (unreadableParts.Count > 0) unreadable?.Add((sensorId, value));

                foreach (var (channel, threshold) in thresholds)
                {
                    if (threshold == 0) continue;   // no threshold: it replaces none read before
                    if (!result.TryGetValue(sensorId, out var ofSensor))
                        result[sensorId] = ofSensor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    ofSensor[channel] = threshold;
                }
            }
            return result;
        }

        /// <summary>
        /// One channel's thresholds, keyed by ElementId, from <see cref="ThresholdsByChannel"/>'s map: the channel name is
        /// trimmed and matched whatever its case; a sensor without a threshold for it is absent.
        /// </summary>
        public static Dictionary<Guid, double> ForChannel(
            IReadOnlyDictionary<Guid, Dictionary<string, double>> byChannel, string channelName)
        {
            ArgumentNullException.ThrowIfNull(byChannel);
            ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

            var channel = channelName.Trim();
            var result = new Dictionary<Guid, double>();
            foreach (var (sensorId, thresholds) in byChannel)
                if (thresholds.TryGetValue(channel, out var threshold))
                    result[sensorId] = threshold;
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
                          && et.Name.Contains(ShackleTypeNamePart))
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
        /// Throws when InfluxDB cannot be read: an empty map would pass for "nothing measured".
        /// </summary>
        public static async Task<Dictionary<Guid, double>> MaxWeightsAsync(
            IReadOnlyCollection<Element> shackleSensors,
            ChannelDataType weightChannelDataType,
            DateTime startDate,
            DateTime endDate,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (shackleSensors == null || shackleSensors.Count == 0 || weightChannelDataType == null)
                return new Dictionary<Guid, double>();

            // One aggregated max per sensor over the whole [startDate, endDate] window.
            return await PerSensorAsync(shackleSensors, weightChannelDataType, startDate, endDate,
                "max", values => values.Max(), cancellationToken);
        }

        /// <summary>
        /// One aggregate of the channel per sensor over the whole [startDate, endDate] window (one row per sensor;
        /// <paramref name="combine"/> joins them should the window ever be cut in two), keyed by ElementId.
        /// Throws when InfluxDB cannot be read.
        /// </summary>
        internal static async Task<Dictionary<Guid, double>> PerSensorAsync(
            IReadOnlyCollection<Element> sensors, ChannelDataType channel, DateTime startDate, DateTime endDate,
            string aggregateFn, Func<IEnumerable<double>, double> combine, CancellationToken cancellationToken)
        {
            var rows = await ReadChannelAsync(sensors, channel, startDate, endDate, WholePeriodWindow, aggregateFn, cancellationToken);

            var perSensor = new Dictionary<Guid, double>();
            foreach (var bySensor in rows.Where(r => r[2] != null).GroupBy(r => r[1] as string))
                if (Guid.TryParse(bySensor.Key, out var sensorId))
                    perSensor[sensorId] = combine(bySensor.Select(r => (double)r[2]));
            return perSensor;
        }

        // One channel of the sensors, aggregated per window: GetData rows ([0] time, [1] sensor id, [2] value).
        internal static Task<List<object[]>> ReadChannelAsync(
            IReadOnlyCollection<Element> sensors, ChannelDataType channel, DateTime startDate, DateTime endDate,
            string aggregateWindow, string aggregateFn, CancellationToken cancellationToken) =>
            InfluxService.GetData(
                sensors.Select(s => s.ElementId.ToString()).ToList(),
                new List<string> { channel.ChannelDataTypeId.ToString() },
                startDate, endDate, ReportsService.BucketName,
                aggregateWindowDuration: aggregateWindow, aggregateFn: aggregateFn,
                cancellationToken: cancellationToken);

        /// <summary>
        /// Per-sensor charge ratio = (max value over [startDate, endDate]) / that sensor's own alarm
        /// threshold for the channel, keyed by ElementId. Sensors with no threshold are omitted —
        /// there is no default rated load to fall back on. Throws when InfluxDB cannot be read.
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
        /// One of the dashboard's curves in % of the alarm threshold: for each week, Monday to Sunday and dated
        /// by its Monday, the highest reading rated against its own sensor's threshold, and the sensor that gave it.
        /// <paramref name="rows"/> are InfluxService.GetData rows: [0] time, [1] sensor id, [2] value. A reading
        /// with no value, or of a sensor with no threshold, is left out; a week with nothing left has no peak. On a
        /// tie the first reading keeps the peak.
        /// </summary>
        public static List<WeeklyPeak> WeeklyPeaks(IEnumerable<object[]> rows, IReadOnlyDictionary<Guid, double> thresholds)
        {
            var peaks = new SortedDictionary<DateTime, WeeklyPeak>();
            foreach (var (at, sensorId, ratio) in RatedReadings(rows, thresholds))
            {
                var week = InfluxService.WeekStart(at);
                if (!peaks.TryGetValue(week, out var peak) || ratio > peak.Ratio)
                    peaks[week] = new WeeklyPeak(week, ratio, sensorId);
            }
            return peaks.Values.ToList();
        }

        // GetData rows ([0] time, [1] sensor id, [2] value) rated against their own sensor's threshold; a row with no
        // value, or of a sensor with no threshold, is left out.
        private static IEnumerable<(DateTime At, Guid SensorId, double Ratio)> RatedReadings(
            IEnumerable<object[]> rows, IReadOnlyDictionary<Guid, double> thresholds)
        {
            foreach (var r in rows)
            {
                if (r[0] is not DateTime at || r[2] == null) continue;
                if (!Guid.TryParse(r[1] as string, out var sensorId)) continue;
                if (Ratio(thresholds, sensorId, (double)r[2]) is double ratio)
                    yield return (at, sensorId, ratio);
            }
        }

        /// <summary>A ratio as a whole percentage, to the nearest (a half away from zero), never clamped: 1.4 is 140.</summary>
        public static int Percent(double ratio) => (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Each rated sensor's highest reading over its own threshold, keyed by ElementId, from GetData rows ([0] time,
        /// [1] sensor id, [2] value). A sensor with no threshold, or no reading, is absent.
        /// </summary>
        public static Dictionary<Guid, double> MaxRatioBySensor(IEnumerable<object[]> rows, IReadOnlyDictionary<Guid, double> thresholds)
        {
            var maxRatioBySensor = new Dictionary<Guid, double>();
            foreach (var (_, sensorId, ratio) in RatedReadings(rows, thresholds))
                if (!maxRatioBySensor.TryGetValue(sensorId, out var max) || ratio > max)
                    maxRatioBySensor[sensorId] = ratio;
            return maxRatioBySensor;
        }

        /// <summary>
        /// The dashboard's shackle gauges from what was read over one period, and how many shackles are rated. A shackle
        /// without a threshold is left out and counted. <paramref name="meanBySensor"/> holds each rated shackle's mean
        /// reading over the period, <paramref name="weeklyMaxRows"/> the weekly maxima (GetData rows) the shackle curves
        /// are drawn from, so the max gauge is the highest weekly peak. With no reading of a rated shackle there are no
        /// gauges (null), never zeros.
        /// </summary>
        public static ShackleCharge Charge(
            IReadOnlyCollection<Guid> shackleIds,
            IReadOnlyDictionary<Guid, double> thresholds,
            IReadOnlyDictionary<Guid, double> meanBySensor,
            IEnumerable<object[]> weeklyMaxRows)
        {
            int rated = shackleIds.Count(id => Ratio(thresholds, id, 0) is not null);
            var maxRatioBySensor = MaxRatioBySensor(weeklyMaxRows, thresholds);
            return new ShackleCharge(Gauges(maxRatioBySensor, thresholds, meanBySensor), rated, shackleIds.Count - rated);
        }

        private static ShackleGauges? Gauges(IReadOnlyDictionary<Guid, double> maxRatioBySensor,
            IReadOnlyDictionary<Guid, double> thresholds, IReadOnlyDictionary<Guid, double> meanBySensor)
        {
            if (maxRatioBySensor.Count == 0) return null;

            var meanRatios = maxRatioBySensor.Keys
                .Select(id => meanBySensor.TryGetValue(id, out var mean) ? Ratio(thresholds, id, mean) : null)
                .OfType<double>()
                .ToList();
            if (meanRatios.Count == 0)
                throw new InvalidOperationException(
                    "The shackles have weekly maxima over the period but no mean: the two reads disagree, no gauge can be drawn.");

            int critical = maxRatioBySensor.Values.Count(r => r > CriticalRatio);
            return new ShackleGauges(
                MeanPercent: Percent(meanRatios.Average()),
                MaxPercent: Percent(maxRatioBySensor.Values.Max()),
                CriticalPercent: Percent((double)critical / maxRatioBySensor.Count));
        }
    }

    /// <summary>One week's peak charge ratio and the sensor that reached it, see SensorChargeRatioHelper.WeeklyPeaks.</summary>
    public record WeeklyPeak(DateTime Week, double Ratio, Guid SensorId);

    /// <summary>The dashboard's three gauges, in whole percent, unclamped (a shackle at 1.4 x its threshold is 140).</summary>
    public record ShackleGauges(int MeanPercent, int MaxPercent, int CriticalPercent);

    /// <summary>
    /// The shackle figures of one period, see SensorChargeRatioHelper.Charge: the gauges (null when no rated shackle was
    /// read in the period), how many shackles have a threshold (Rated) and how many were left out for lack of one
    /// (WithoutThreshold).
    /// </summary>
    public record ShackleCharge(ShackleGauges? Gauges, int Rated, int WithoutThreshold);
}
