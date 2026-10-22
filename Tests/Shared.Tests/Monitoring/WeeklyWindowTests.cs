using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's weekly graphs take one maximum (or minimum) per week from InfluxDB.
/// What matters: a week is Monday to Sunday and its row carries that Monday, so a peak sits under the week it
/// happened in. Plain "1w" windows run Thursday to Wednesday and carry the following Thursday, and shifting them
/// with offset -3d stops InfluxDB aggregating in its storage engine (3 months of 81 shackles: 66.6 s instead of
/// 3.3 s, measured 2026-10-23). So a week is read as its days and the days are folded into weeks here.
/// </summary>
public class WeeklyWindowTests
{
    static readonly string A = "sensor-a", B = "sensor-b";

    static DateTime Utc(string at) =>
        DateTime.SpecifyKind(DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    // A GetData row of two channels: [time, sensor, channel 1, channel 2]; null = no reading of that channel.
    static object[] Day(string at, string sensor, double? c1, double? c2) => new object[] { Utc(at), sensor, c1, c2 };

    [Theory]
    [InlineData("2026-09-28T00:00:00", "2026-09-28")]   // Monday 00:00:00 starts its own week
    [InlineData("2026-09-30T13:45:00", "2026-09-28")]   // Wednesday
    [InlineData("2026-10-04T23:59:59", "2026-09-28")]   // Sunday 23:59:59 still ends it
    [InlineData("2026-10-05T00:00:00", "2026-10-05")]   // next Monday
    public void A_row_belongs_to_the_week_of_its_Monday(string at, string monday)
    {
        var instant = DateTime.SpecifyKind(DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

        Assert.Equal(DateTime.Parse(monday, System.Globalization.CultureInfo.InvariantCulture), InfluxService.WeekStart(instant));
    }

    [Theory]
    [InlineData("max")]
    [InlineData("min")]
    public void A_weekly_window_is_read_as_days_dated_by_their_start_without_an_offset(string fn)
    {
        var clause = InfluxService.AggregateWindowClause(InfluxService.WeeklyWindow, fn);

        Assert.Equal($"|> aggregateWindow(every: 1d, timeSrc: \"_start\", fn: {fn}, createEmpty: false)", clause);
        Assert.DoesNotContain("offset", clause);
    }

    [Theory]
    [InlineData("mean")]
    [InlineData("sum")]
    [InlineData("last")]
    public void A_weekly_window_that_days_cannot_make_is_refused(string fn)
    {
        // The mean of a week is not the mean of its daily means: folding days would give a wrong figure.
        var refused = Assert.ThrowsAny<ArgumentException>(() => InfluxService.AggregateWindowClause(InfluxService.WeeklyWindow, fn));
        Assert.Contains(fn, refused.Message);
    }

    [Fact]
    public void Days_fold_into_their_Monday_weeks_with_the_maximum_of_each_channel()
    {
        var days = new List<object[]>
        {
            Day("2026-09-02T10:00:00", A, 5, null),     // Wednesday: the first week, cut by the range start
            Day("2026-09-06T00:00:00", A, 9, 1),        // Sunday: still the week of 31 August
            Day("2026-09-07T00:00:00", A, 7, null),     // Monday: the next week
            Day("2026-09-10T00:00:00", A, -3, 40),
        };

        var weeks = InfluxService.FoldDaysIntoWeeks(days, "max");

        Assert.Equal(2, weeks.Count);
        Assert.Equal(new object[] { Utc("2026-08-31"), A, 9.0, 1.0 }, weeks[0]);
        Assert.Equal(new object[] { Utc("2026-09-07"), A, 7.0, 40.0 }, weeks[1]);
    }

    [Fact]
    public void Days_fold_with_the_minimum_and_keep_a_negative_value()
    {
        var days = new List<object[]>
        {
            Day("2026-09-07T00:00:00", A, 2, 0.5),
            Day("2026-09-08T00:00:00", A, -1.8, 0.4),
            Day("2026-09-13T00:00:00", A, 0.1, -0.6),   // Sunday
        };

        var week = Assert.Single(InfluxService.FoldDaysIntoWeeks(days, "min"));

        Assert.Equal(new object[] { Utc("2026-09-07"), A, -1.8, -0.6 }, week);
    }

    [Fact]
    public void A_channel_without_a_reading_in_a_week_stays_without_one()
    {
        var days = new List<object[]> { Day("2026-09-07T00:00:00", A, 3, null), Day("2026-09-09T00:00:00", A, 4, null) };

        var week = Assert.Single(InfluxService.FoldDaysIntoWeeks(days, "max"));

        Assert.Null(week[3]);   // never a 0 for a channel nothing was read on
    }

    [Fact]
    public void Each_sensor_keeps_its_own_weeks_in_the_order_they_were_read()
    {
        var days = new List<object[]>
        {
            Day("2026-09-01T00:00:00", B, 1, null), Day("2026-09-08T00:00:00", B, 2, null),
            Day("2026-09-01T00:00:00", A, 10, null), Day("2026-09-08T00:00:00", A, 20, null),
        };

        var weeks = InfluxService.FoldDaysIntoWeeks(days, "max");

        Assert.Equal(new[] { (B, Utc("2026-08-31")), (B, Utc("2026-09-07")), (A, Utc("2026-08-31")), (A, Utc("2026-09-07")) },
            weeks.Select(w => ((string)w[1], (DateTime)w[0])));
        Assert.All(weeks, w => Assert.Equal(DateTimeKind.Utc, ((DateTime)w[0]).Kind));
    }

    [Fact]
    public void Folding_with_a_function_days_cannot_make_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            InfluxService.FoldDaysIntoWeeks(new List<object[]> { Day("2026-09-07T00:00:00", A, 1, 1) }, "mean"));
    }

    [Theory]
    [InlineData("36500d", "mean")]
    [InlineData("7d", "max")]
    [InlineData("15m", "max")]
    public void Other_windows_are_left_as_Flux_cuts_them(string every, string fn)
    {
        var clause = InfluxService.AggregateWindowClause(every, fn);

        Assert.Equal($"|> aggregateWindow(every: {every}, fn: {fn}, createEmpty: false)", clause);
    }

    [Theory]
    [InlineData(null, "max")]
    [InlineData("1w", "")]
    public void A_missing_window_or_function_is_refused(string? every, string? fn)
    {
        Assert.ThrowsAny<ArgumentException>(() => InfluxService.AggregateWindowClause(every, fn));
    }
}
