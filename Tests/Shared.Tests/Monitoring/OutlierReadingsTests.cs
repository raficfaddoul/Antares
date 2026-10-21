using System.Globalization;
using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// Deleting an outlier from the Surveillance plot traces a plotted point back to the raw readings behind it. What
/// matters: the window found is exactly the one GetData's downsampling aggregated into that point (same step, laid
/// from the Unix epoch, cut by the range, dated by its stop), and only readings holding the point's very value are
/// taken from it.
/// </summary>
public class OutlierReadingsTests
{
    static DateTime U(string at) =>
        DateTime.SpecifyKind(DateTime.Parse(at, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    [Theory]
    [InlineData(3600, 1000, "3s", 3_000)]                  // last hour, default budget: 3.6 s
    [InlineData(86_400, 1000, "1m", 60_000)]               // last day: 86.4 s
    [InlineData(604_800, 1000, "10m", 600_000)]            // last week: 604.8 s
    [InlineData(2_592_000, 1000, "43m", 2_580_000)]        // 30 days: 43.2 min
    [InlineData(31_536_000, 1000, "8h", 28_800_000)]       // 365 days: 8.76 h
    [InlineData(3600, 100_000, "36ms", 36)]                // last hour, largest budget: 36 ms
    [InlineData(315_360_000, 100, "36d", 3_110_400_000)]   // ten years, smallest budget: 36.5 d
    public void The_downsampling_step_is_the_span_over_the_budget_cut_to_a_whole_unit(
        int spanSeconds, int maxDataPoints, string flux, long everyMs)
    {
        var start = U("2026-10-21T08:00:00");

        var (actualFlux, every) = InfluxService.DownsampleWindow(start, start.AddSeconds(spanSeconds), maxDataPoints);

        Assert.Equal(flux, actualFlux);
        Assert.Equal(TimeSpan.FromMilliseconds(everyMs), every);
    }

    [Fact]
    public void A_plot_without_points_has_no_step()
    {
        var start = U("2026-10-21T08:00:00");

        Assert.Throws<ArgumentOutOfRangeException>(() => InfluxService.DownsampleWindow(start, start.AddHours(1), 0));
    }

    [Theory]
    // A whole window inside the range: the step before the row's time.
    [InlineData("2026-10-21T10:03:00", 60, "2026-10-21T09:00:30", "2026-10-21T10:02:00")]
    // The first window, cut by the range's start.
    [InlineData("2026-10-21T10:03:00", 60, "2026-10-21T10:02:30.5", "2026-10-21T10:02:30.5")]
    // The last window, cut by the range's stop: dated by that stop, it starts at the step holding it.
    [InlineData("2026-10-21T10:04:17.123", 60, "2026-10-21T09:00:00", "2026-10-21T10:04:00")]
    // An 8 h step lies on the epoch's midnight, not on the range's start.
    [InlineData("2026-10-21T16:00:00", 28_800, "2026-10-20T03:00:00", "2026-10-21T08:00:00")]
    public void A_rows_window_ends_at_its_time_and_starts_on_the_epoch_aligned_step(
        string rowTime, int everySeconds, string rangeStart, string expectedStart)
    {
        var (start, stop) = InfluxService.AggregatedWindowOf(U(rowTime), TimeSpan.FromSeconds(everySeconds), U(rangeStart));

        Assert.Equal(U(expectedStart), start);
        Assert.Equal(U(rowTime), stop);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void Steps_that_do_not_divide_a_minute_still_lie_on_the_epoch()
    {
        // 86 s steps: the boundaries are whole multiples of 86 s since 1970-01-01.
        var boundary = DateTime.UnixEpoch.AddSeconds(86L * 20_513_000);

        var (start, stop) = InfluxService.AggregatedWindowOf(boundary, TimeSpan.FromSeconds(86), boundary.AddDays(-1));

        Assert.Equal(boundary.AddSeconds(-86), start);
        Assert.Equal(boundary, stop);
    }

    [Fact]
    public void A_window_is_refused_for_what_no_plot_could_have_drawn()
    {
        var at = U("2026-10-21T10:00:00");

        Assert.Throws<ArgumentOutOfRangeException>(() => InfluxService.AggregatedWindowOf(at, TimeSpan.Zero, at.AddHours(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InfluxService.AggregatedWindowOf(at, TimeSpan.FromMinutes(1), at));
        Assert.Throws<ArgumentException>(() => InfluxService.AggregatedWindowOf(
            DateTime.SpecifyKind(at, DateTimeKind.Unspecified), TimeSpan.FromMinutes(1), at.AddHours(-1)));
    }

    [Fact]
    public void Only_readings_holding_the_points_very_value_are_taken()
    {
        string sensor = Guid.NewGuid().ToString();
        var rows = new List<object[]>
        {
            new object[] { U("2026-10-21T10:00:03"), sensor, 1520.5 },
            new object[] { U("2026-10-21T10:00:01"), sensor, 9999.0 },
            new object[] { U("2026-10-21T10:00:02"), sensor, 9999.000000001 },   // close is not equal
            new object[] { U("2026-10-21T10:00:04"), sensor, null! },            // no value for this channel
            new object[] { U("2026-10-21T10:00:00"), sensor, 9999.0 },
            new object[] { U("2026-10-21T10:00:00"), sensor, 9999.0 },           // the same instant twice
        };

        var instants = OutlierReadings.InstantsWithValue(rows, 9999.0);

        Assert.Equal(new[] { U("2026-10-21T10:00:00"), U("2026-10-21T10:00:01") }, instants);
    }
}
