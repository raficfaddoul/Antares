using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AliantWebS.Data
{
    /// <summary>
    /// Deleting an outlier from the Surveillance plot (Developer role). A plotted point is the maximum of a window of
    /// raw readings (GetData downsamples with fn: max), so the readings behind it are those of its window whose
    /// reported value is that maximum: usually one, several when the maximum repeats. InfluxDB deletes by tag and
    /// time, never by field, so deleting a reading takes every channel the sensor reported at that instant.
    /// </summary>
    public static class OutlierReadings
    {
        /// <summary>The plot a point comes from: the range and point budget GetData was called with, and its bucket.</summary>
        public sealed record Plot(DateTime Start, DateTime End, int MaxDataPoints, string Bucket);

        /// <summary>
        /// The raw readings window [WindowStart, WindowStop) behind a plotted point, how many readings of the point's
        /// channel it holds, and the instants (UTC, in order) of those whose value is the point's.
        /// </summary>
        public sealed record Found(DateTime WindowStart, DateTime WindowStop, int ReadingsInWindow, IReadOnlyList<DateTime> Instants);

        /// <summary>
        /// The readings behind the point a plot drew at pointTime (UTC, as GetData dated it) with pointValue, for one
        /// sensor's channel. The values compared are the reported ones on both sides (GetData calibrates the raw
        /// readings with the same expression), so they are equal to the bit. Throws when InfluxDB cannot be read.
        /// </summary>
        public static async Task<Found> FindAsync(Guid sensorId, Guid channelId, DateTime pointTime, double pointValue, Plot plot)
        {
            var every = InfluxService.DownsampleWindow(plot.Start, plot.End, plot.MaxDataPoints).Every;
            // In UTC, as GetData sends the range to InfluxDB.
            var (start, stop) = InfluxService.AggregatedWindowOf(pointTime, every, plot.Start.ToUniversalTime());

            var rows = await InfluxService.GetData(
                new List<string> { sensorId.ToString() }, new List<string> { channelId.ToString() }, start, stop, plot.Bucket);

            return new Found(start, stop, rows.Count, InstantsWithValue(rows, pointValue));
        }

        /// <summary>The distinct instants, in order, of the GetData rows of one sensor's channel whose value is value.</summary>
        public static IReadOnlyList<DateTime> InstantsWithValue(IEnumerable<object[]> rows, double value) =>
            rows.Where(r => r[2] is double v && v == value)
                .Select(r => (DateTime)r[0])
                .Distinct()
                .OrderBy(at => at)
                .ToList();

        /// <summary>
        /// Deletes the sensor's whole readings at those instants (UTC), then reads the channel back over them: a
        /// reading still there throws, rather than passing for deleted, since InfluxDB answers a delete that matched
        /// nothing as it answers one that worked.
        /// </summary>
        public static async Task DeleteAsync(Guid sensorId, Guid channelId, IReadOnlyList<DateTime> instants, string bucket)
        {
            if (instants.Count == 0)
                throw new ArgumentException("No reading to delete.", nameof(instants));

            await InfluxService.DeleteSensorReadingsAsync(bucket, sensorId, instants);

            var rows = await InfluxService.GetData(
                new List<string> { sensorId.ToString() }, new List<string> { channelId.ToString() },
                instants.Min(), instants.Max().AddTicks(1), bucket);
            var left = rows.Select(r => (DateTime)r[0]).Intersect(instants).OrderBy(at => at).ToList();
            if (left.Count > 0)
                throw new InvalidOperationException(
                    $"InfluxDB accepted the delete, but {left.Count} reading(s) are still there: "
                    + string.Join(", ", left.Select(at => at.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture))) + ".");
        }
    }
}
