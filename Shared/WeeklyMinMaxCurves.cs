using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AliantWebS.Models;

namespace AliantWebS.Data
{
    /// <summary>
    /// The dashboard's bottom-right graph (decision 12 of the 2026-10-22 redesign): for one sensor type, the weekly
    /// minimum and maximum of every channel across the type's sensors, each channel's two series on the axis the shared
    /// rule <see cref="PlotAxes"/> gives it. A wind direction is left out, since a weekly minimum or maximum of an angle
    /// means nothing; the channels without a reading or without a recorded unit are listed, for the page to say. No unit
    /// is ever put in a series name or an axis label. What would mislabel the graph or hide a broken read is refused.
    /// </summary>
    public static class WeeklyMinMaxCurves
    {
        /// <summary>The channel never plotted: a weekly min/max of an angle means nothing (decision 12).</summary>
        public const string DirectionChannel = "direction";

        /// <summary>Decimals the series keep (the CSV sent to the browser).</summary>
        public const int Decimals = 4;

        // The InfluxDB aggregate functions of the two reads.
        private const string MinFn = "min", MaxFn = "max";

        // GetData rows: [0] time, [1] sensor id, then the channels asked for, in order.
        private const int TimeColumn = 0, FirstChannelColumn = 2;

        public static string MinSeries(string channel) => $"{channel} (min)";
        public static string MaxSeries(string channel) => $"{channel} (max)";

        public static bool IsDirection(string channel) =>
            string.Equals(channel, DirectionChannel, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The type's channels, distinct by id, ordered by name (ordinal): the directions left out, the rest plotted.
        /// Refuses a type given without its channels (ArgumentException), and, with InvalidOperationException, a plotted
        /// channel whose name holds a comma (the graph's CSV separator) or two plotted channels of one name.
        /// </summary>
        public static TypeChannels ChannelsOf(ElementType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.ChannelDataTypeElementTypes.Any(link => link.ChannelDataType == null))
                throw new ArgumentException(
                    $"The type {type.ElementTypeId} was given without its channels: its curve cannot be planned.", nameof(type));

            var channels = type.ChannelDataTypeElementTypes
                .Select(link => link.ChannelDataType)
                .DistinctBy(channel => channel.ChannelDataTypeId)
                .OrderBy(channel => channel.Name, StringComparer.Ordinal)
                .ToList();
            var plotted = channels.Where(channel => !IsDirection(channel.Name)).ToList();
            RefuseNamesThatBreakTheGraph(type, plotted);
            return new TypeChannels(plotted, channels.Where(channel => IsDirection(channel.Name)).ToList());
        }

        /// <summary>
        /// The series from the weekly-min and weekly-max rows of <paramref name="plotted"/> (GetData rows: [0] a UTC time,
        /// [1] the sensor, [2..] the plotted channels in order; a null cell is no reading). For each channel, its lowest
        /// weekly minimum and highest weekly maximum across the sensors, per week (by its Monday), rounded to
        /// <see cref="Decimals"/>; a series is added only when it has a value, min before max. A row of the wrong shape
        /// is refused (ArgumentException), never skipped.
        /// </summary>
        public static WeeklyMinMax Build(IReadOnlyList<ChannelDataType> plotted,
            IReadOnlyList<object[]> minRows, IReadOnlyList<object[]> maxRows)
        {
            ArgumentNullException.ThrowIfNull(plotted);
            RefuseMalformedRows(minRows, plotted.Count, nameof(minRows));
            RefuseMalformedRows(maxRows, plotted.Count, nameof(maxRows));

            var indices = new List<Dictionary<object, double>>();
            var headers = new List<string> { "Date" };
            var curves = new List<(string Series, string Channel)>();
            var withoutReading = new List<string>();
            var withoutUnit = new List<string>();

            void Add(Dictionary<object, double> series, string name, string channel)
            {
                if (series.Count == 0) return;
                indices.Add(series);
                headers.Add(name);
                curves.Add((name, channel));
            }

            for (int i = 0; i < plotted.Count; i++)
            {
                var channel = plotted[i];
                int column = FirstChannelColumn + i;
                var min = Weekly(minRows, column, values => values.Min());
                var max = Weekly(maxRows, column, values => values.Max());
                if (min.Count == 0 && max.Count == 0)
                {
                    withoutReading.Add(channel.Name);
                    continue;
                }
                Add(min, MinSeries(channel.Name), channel.Name);
                Add(max, MaxSeries(channel.Name), channel.Name);
                if (string.IsNullOrWhiteSpace(channel.Units)) withoutUnit.Add(channel.Name);
            }

            return new WeeklyMinMax(indices, headers, PlotAxes.For(curves), withoutReading, withoutUnit);
        }

        /// <summary>
        /// Reads the weekly minima and maxima of <paramref name="plotted"/> over the sensors, both together on one token,
        /// and builds the series. Refuses, before any read, no channel, no sensor or a direction channel
        /// (ArgumentException): the caller says those cases itself. Throws what the reads throw (InfluxReadException,
        /// OperationCanceledException): never an empty curve in place of a failure.
        /// </summary>
        public static async Task<WeeklyMinMax> ReadAsync(IReadOnlyCollection<Element> sensors,
            IReadOnlyList<ChannelDataType> plotted, DateTime start, DateTime end, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sensors);
            ArgumentNullException.ThrowIfNull(plotted);
            if (plotted.Count == 0)
                throw new ArgumentException("No channel to read: the curve cannot be read.", nameof(plotted));
            if (sensors.Count == 0)
                throw new ArgumentException("No sensor to read: the curve cannot be read.", nameof(sensors));
            if (plotted.FirstOrDefault(channel => IsDirection(channel.Name)) is ChannelDataType direction)
                throw new ArgumentException($"The channel '{direction.Name}' is a direction: it is never read.", nameof(plotted));

            var sensorIds = sensors.Select(s => s.ElementId.ToString()).ToList();
            var channelIds = plotted.Select(c => c.ChannelDataTypeId.ToString()).ToList();
            // GetData only reads the id lists, so the two concurrent reads share them.
            Task<List<object[]>> Read(string aggregateFn) => InfluxService.GetData(
                sensorIds, channelIds, start, end, ReportsService.BucketName,
                aggregateWindowDuration: InfluxService.WeeklyWindow, aggregateFn: aggregateFn,
                cancellationToken: cancellationToken);

            var minRead = Read(MinFn);
            var maxRead = Read(MaxFn);
            await Task.WhenAll(minRead, maxRead);
            return Build(plotted, minRead.Result, maxRead.Result);
        }

        // Per week (its Monday), the combined values of one column; weeks without a value are absent.
        private static Dictionary<object, double> Weekly(IReadOnlyList<object[]> rows, int column,
            Func<IEnumerable<double>, double> combine) =>
            rows.Where(row => row[column] != null)
                .GroupBy(row => InfluxService.WeekStart((DateTime)row[TimeColumn]))
                .OrderBy(week => week.Key)
                .ToDictionary(week => (object)week.Key,
                    week => Math.Round(combine(week.Select(row => (double)row[column])), Decimals));

        private static void RefuseMalformedRows(IReadOnlyList<object[]> rows, int channels, string name)
        {
            ArgumentNullException.ThrowIfNull(rows, name);
            int length = FirstChannelColumn + channels;
            foreach (var row in rows)
            {
                if (row == null || row.Length != length)
                    throw new ArgumentException(
                        $"A row has {row?.Length.ToString() ?? "no"} cells where {length} were expected (time, sensor, {channels} channel(s)).", name);
                if (row[TimeColumn] is not DateTime)
                    throw new ArgumentException($"A row's time is not a DateTime: '{row[TimeColumn]}'.", name);
                for (int column = FirstChannelColumn; column < length; column++)
                    if (row[column] is not (null or double))
                        throw new ArgumentException($"A row's value is not a number: '{row[column]}'.", name);
            }
        }

        // DisplayData joins the headers with commas and cannot tell two series of one name apart.
        private static void RefuseNamesThatBreakTheGraph(ElementType type, IReadOnlyList<ChannelDataType> plotted)
        {
            if (plotted.FirstOrDefault(channel => channel.Name.Contains(',')) is ChannelDataType withComma)
                throw new InvalidOperationException(
                    $"La voie « {withComma.Name} » du type « {AlarmRatioCurves.TypeName(type)} » ne peut être tracée : "
                    + "son nom contient une virgule, le séparateur des données du graphique.");

            if (plotted.GroupBy(channel => channel.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
                throw new InvalidOperationException(
                    $"Le type « {AlarmRatioCurves.TypeName(type)} » a deux voies nommées « {twice.Key} » : "
                    + "leurs courbes ne pourraient être distinguées.");
        }
    }

    /// <summary>A type's channels: those the graph plots, and the directions it leaves out (both by name).</summary>
    public sealed record TypeChannels(IReadOnlyList<ChannelDataType> Plotted, IReadOnlyList<ChannelDataType> LeftOut);

    /// <summary>
    /// The bottom-right graph's data: the series (Indices[k] belongs to Headers[k + 1], Headers[0] is "Date"), their axes,
    /// and the plotted channels without a reading or, among the drawn ones, without a recorded unit (plot order).
    /// </summary>
    public sealed record WeeklyMinMax(List<Dictionary<object, double>> Indices, List<string> Headers, PlotAxes Axes,
        IReadOnlyList<string> WithoutReading, IReadOnlyList<string> WithoutUnit)
    {
        public bool HasSeries => Indices.Count > 0;
    }
}
