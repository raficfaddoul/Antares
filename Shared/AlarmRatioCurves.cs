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
    /// The dashboard's top-right graph and plan colours for every sensor type with an alarm threshold (decisions 10 and
    /// 11 of the 2026-10-22 redesign). One curve per (sensor type, channel) that has a "Seuil Alarme" on at least one
    /// sensor of the structure: a type joins by its data alone, since the threshold names its channel. Each weekly value
    /// is the highest reading over that sensor's own threshold for the channel (SensorChargeRatioHelper.WeeklyPeaks);
    /// each rated sensor is coloured by its worst channel; the gauges stay the shackles' (SensorChargeRatioHelper.Charge),
    /// rated from the same weight rows and thresholds as the shackle curves, so the max gauge is their highest peak.
    /// </summary>
    public static class AlarmRatioCurves
    {
        /// <summary>
        /// The curves the thresholds call for, ordered by type name, then channel name (ordinal). Each threshold's
        /// channel is matched, whatever its case, against the channels of its sensor's own type: a channel the type does
        /// not have draws nothing and is added to <paramref name="channelsTheTypeLacks"/>. Every sensor must carry its
        /// type with its channels (ArgumentException otherwise). Refuses, with InvalidOperationException, what would
        /// mislabel the graph: two curves of one name, a comma in a name (the graph's CSV separator), a threshold that
        /// matches two of its type's channels.
        /// </summary>
        public static IReadOnlyList<AlarmCurve> Plan(
            IReadOnlyCollection<Element> sensors,
            IReadOnlyDictionary<Guid, Dictionary<string, double>> thresholdsBySensor,
            ICollection<(Guid SensorId, string Channel)> channelsTheTypeLacks)
        {
            ArgumentNullException.ThrowIfNull(sensors);
            ArgumentNullException.ThrowIfNull(thresholdsBySensor);
            ArgumentNullException.ThrowIfNull(channelsTheTypeLacks);

            // By type id, not by instance: an AsNoTracking load gives each sensor its own ElementType object.
            var typeNames = new Dictionary<Guid, string>();
            var sensorsOfType = new Dictionary<Guid, int>();
            var thresholdsByCurve = new Dictionary<(Guid TypeId, Guid ChannelId), (ChannelDataType Channel, Dictionary<Guid, double> Thresholds)>();

            foreach (var sensor in sensors)
            {
                var type = TypeOf(sensor);
                typeNames.TryAdd(type.ElementTypeId, SensorTypeCurves.TypeName(type));
                sensorsOfType[type.ElementTypeId] = sensorsOfType.GetValueOrDefault(type.ElementTypeId) + 1;

                if (!thresholdsBySensor.TryGetValue(sensor.ElementId, out var thresholds)) continue;
                foreach (var (channelName, threshold) in thresholds)
                {
                    if (ChannelOf(type, channelName) is not ChannelDataType channel)
                    {
                        channelsTheTypeLacks.Add((sensor.ElementId, channelName));
                        continue;
                    }
                    var key = (type.ElementTypeId, channel.ChannelDataTypeId);
                    if (!thresholdsByCurve.TryGetValue(key, out var curve))
                        thresholdsByCurve[key] = curve = (channel, new Dictionary<Guid, double>());
                    curve.Thresholds[sensor.ElementId] = threshold;
                }
            }

            var curves = thresholdsByCurve
                .Select(kv => new AlarmCurve(typeNames[kv.Key.TypeId], kv.Value.Channel,
                    kv.Value.Thresholds, sensorsOfType[kv.Key.TypeId]))
                .OrderBy(c => c.TypeName, StringComparer.Ordinal)
                .ThenBy(c => c.Channel.Name, StringComparer.Ordinal)
                .ToList();
            SensorTypeCurves.RefuseUndrawableNames(curves, c => c.Name,
                withComma: c => $"La courbe « {c.Name} » ne peut être tracée : son nom contient une virgule, le séparateur des données du graphique.",
                twice: name => $"Deux types de capteurs donneraient la même courbe « {name} » : leurs points ne pourraient être distingués.");
            return curves;
        }

        /// <summary>
        /// Each curve's weekly peaks over the rows read for its channel (kept even when empty: a curve without a reading
        /// is said, not dropped), and each rated sensor's worst ratio across its curves. <paramref name="weeklyMaxRowsByChannelId"/>
        /// holds GetData rows (one value column) per channel id. A curve whose channel was not read is
        /// refused (ArgumentException): an empty curve must never stand in for a read that did not happen.
        /// </summary>
        public static AlarmRatioReading Rate(
            IReadOnlyList<AlarmCurve> curves, IReadOnlyDictionary<Guid, List<object[]>> weeklyMaxRowsByChannelId)
        {
            ArgumentNullException.ThrowIfNull(curves);
            ArgumentNullException.ThrowIfNull(weeklyMaxRowsByChannelId);

            var rated = new List<AlarmCurvePeaks>();
            var worstRatioBySensor = new Dictionary<Guid, double>();
            foreach (var curve in curves)
            {
                if (!weeklyMaxRowsByChannelId.TryGetValue(curve.Channel.ChannelDataTypeId, out var rows))
                    throw new ArgumentException(
                        $"The channel of the curve '{curve.Name}' was not read: it cannot be rated.", nameof(weeklyMaxRowsByChannelId));

                rated.Add(new AlarmCurvePeaks(curve, SensorChargeRatioHelper.WeeklyPeaks(rows, curve.Thresholds)));
                foreach (var (sensorId, ratio) in SensorChargeRatioHelper.MaxRatioBySensor(rows, curve.Thresholds))
                    if (!worstRatioBySensor.TryGetValue(sensorId, out var worst) || ratio > worst)
                        worstRatioBySensor[sensorId] = ratio;
            }
            return new AlarmRatioReading(rated, worstRatioBySensor, Shackles: null);
        }

        /// <summary>
        /// Reads the dashboard's curves, plan colours and shackle gauges over [start, end]. The thresholds first (SQL),
        /// since they decide which curves exist and what to read; then, unless <paramref name="cancellationToken"/> was
        /// cancelled meanwhile, InfluxDB together: one weekly-max read per rated channel over the sensors rated on it (the
        /// weight channel's also over the rated shackles), and the rated shackles' mean over the period. Nothing rated:
        /// InfluxDB is not read. <see cref="AlarmRatioReading.Shackles"/> is null when there is no shackle or no weight
        /// channel. Unreadable thresholds go to <paramref name="unreadable"/>, thresholds naming a channel their type lacks
        /// to <paramref name="channelsTheTypeLacks"/>. Throws what its reads throw (InfluxReadException, SQL errors,
        /// OperationCanceledException): never an empty reading in place of a failure.
        /// </summary>
        public static async Task<AlarmRatioReading> ReadAsync(
            IDbContextFactory<AliantWebContext> dbContextFactory,
            IReadOnlyCollection<Element> sensors,
            IReadOnlyCollection<Element> shackles,
            ChannelDataType? weightChannel,
            DateTime start,
            DateTime end,
            ICollection<(Guid ElementId, string Value)> unreadable,
            ICollection<(Guid SensorId, string Channel)> channelsTheTypeLacks,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dbContextFactory);
            ArgumentNullException.ThrowIfNull(sensors);
            ArgumentNullException.ThrowIfNull(shackles);
            ArgumentNullException.ThrowIfNull(unreadable);
            ArgumentNullException.ThrowIfNull(channelsTheTypeLacks);

            // The shackles too, should the page's category test and the helper's ever disagree: a shackle never loses
            // its threshold.
            var everyone = sensors.UnionBy(shackles, s => s.ElementId).ToList();
            var thresholds = await SensorChargeRatioHelper.LoadThresholdsByChannelAsync(
                dbContextFactory, everyone, unreadable, cancellationToken);
            var curves = Plan(sensors, thresholds, channelsTheTypeLacks);

            bool gauges = shackles.Count > 0 && weightChannel != null;
            var shackleIds = shackles.Select(s => s.ElementId).ToHashSet();
            var shackleThresholds = gauges
                ? SensorChargeRatioHelper.ForChannel(
                    thresholds.Where(kv => shackleIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
                    weightChannel!.Name)
                : new Dictionary<Guid, double>();
            var ratedShackles = shackles.Where(s => shackleThresholds.ContainsKey(s.ElementId)).ToList();

            cancellationToken.ThrowIfCancellationRequested();   // a superseded load never reaches InfluxDB

            var weeklyMaxReads = WhatToRead(curves, weightChannel, ratedShackles).ToDictionary(
                read => read.Channel.ChannelDataTypeId,
                read => SensorChargeRatioHelper.ReadChannelAsync(
                    everyone.Where(s => read.SensorIds.Contains(s.ElementId)).ToList(), read.Channel, start, end,
                    InfluxService.WeeklyWindow, InfluxService.AggregateMax, cancellationToken));
            var meansRead = ratedShackles.Count > 0
                ? SensorChargeRatioHelper.PerSensorAsync(ratedShackles, weightChannel!, start, end,
                    InfluxService.AggregateMean, cancellationToken)
                : null;
            var reads = weeklyMaxReads.Values.Cast<Task>().ToList();
            if (meansRead != null) reads.Add(meansRead);
            await Task.WhenAll(reads);
            cancellationToken.ThrowIfCancellationRequested();

            var rowsByChannelId = weeklyMaxReads.ToDictionary(kv => kv.Key, kv => kv.Value.Result);
            var reading = Rate(curves, rowsByChannelId);
            if (!gauges) return reading;

            var shackleCharge = meansRead == null
                // No rated shackle: nothing was read for the gauges, and Charge says so (Rated 0, no gauges), never zeros.
                ? SensorChargeRatioHelper.Charge(shackleIds, shackleThresholds, new Dictionary<Guid, double>(), new List<object[]>())
                : SensorChargeRatioHelper.Charge(shackleIds, shackleThresholds, meansRead.Result,
                    rowsByChannelId[weightChannel!.ChannelDataTypeId]);
            return reading with { Shackles = shackleCharge };
        }

        // One read per distinct rated channel, over the sensors rated on it; the weight channel's also over the rated
        // shackles, whose gauges come from the same rows as the shackle curves.
        private static IEnumerable<(ChannelDataType Channel, HashSet<Guid> SensorIds)> WhatToRead(
            IReadOnlyList<AlarmCurve> curves, ChannelDataType? weightChannel, IReadOnlyCollection<Element> ratedShackles)
        {
            var reads = new Dictionary<Guid, (ChannelDataType Channel, HashSet<Guid> SensorIds)>();
            void Add(ChannelDataType channel, IEnumerable<Guid> sensorIds)
            {
                if (!reads.TryGetValue(channel.ChannelDataTypeId, out var read))
                    reads[channel.ChannelDataTypeId] = read = (channel, new HashSet<Guid>());
                read.SensorIds.UnionWith(sensorIds);
            }

            foreach (var curve in curves) Add(curve.Channel, curve.Thresholds.Keys);
            if (ratedShackles.Count > 0) Add(weightChannel!, ratedShackles.Select(s => s.ElementId));
            return reads.Values;
        }

        private static ElementType TypeOf(Element sensor)
        {
            var type = sensor.ElementType
                ?? throw new ArgumentException($"The sensor {sensor.ElementId} was given without its type: its thresholds cannot be matched to its channels.");
            SensorTypeCurves.ChannelsOf(type, "its thresholds cannot be matched");   // refuses a type without its channels
            return type;
        }

        // The channel of the type the threshold names, whatever its case; null when the type has none of that name.
        private static ChannelDataType? ChannelOf(ElementType type, string channelName)
        {
            var matches = SensorTypeCurves.ChannelsOf(type, "its thresholds cannot be matched")
                .Where(channel => string.Equals(channel.Name, channelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"Le seuil d'alarme « {channelName} » du type « {SensorTypeCurves.TypeName(type)} » correspond à plusieurs de ses voies "
                    + $"({string.Join(", ", matches.Select(c => c.Name))}) : sa courbe ne peut être tracée.");
            return matches.SingleOrDefault();
        }
    }

    /// <summary>
    /// One curve of the top-right graph: a sensor type's channel, the thresholds of the type's sensors rated on it
    /// (ElementId → threshold), and how many sensors the type has on the structure.
    /// </summary>
    public record AlarmCurve(string TypeName, ChannelDataType Channel,
        IReadOnlyDictionary<Guid, double> Thresholds, int SensorsOfType)
    {
        /// <summary>The series name: "{type} - {channel}", with the channel's own spelling.</summary>
        public string Name => $"{TypeName} - {Channel.Name}";

        /// <summary>The type's sensors left out of the curve for lack of a threshold for its channel.</summary>
        public int WithoutThreshold => SensorsOfType - Thresholds.Count;
    }

    /// <summary>A curve and its weekly peaks over one period (none when nothing was read).</summary>
    public record AlarmCurvePeaks(AlarmCurve Curve, IReadOnlyList<WeeklyPeak> Peaks);

    /// <summary>
    /// What the dashboard draws from one period's reads: the curves with their peaks, each rated sensor's worst ratio
    /// (the plan's colours; a sensor absent is grey) and the shackles' gauges (null with no shackle or no weight channel).
    /// </summary>
    public record AlarmRatioReading(IReadOnlyList<AlarmCurvePeaks> Curves,
        IReadOnlyDictionary<Guid, double> WorstRatioBySensor, ShackleCharge? Shackles);
}
