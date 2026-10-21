using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's two weekly graphs (shackle load, and the one under it) take one maximum per week from InfluxDB.
/// What matters: a week is Monday to Sunday and its row carries that Monday, so a peak sits under the week it
/// happened in. Plain "1w" windows run Thursday to Wednesday and carry the following Thursday.
/// </summary>
public class WeeklyWindowTests
{
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

    [Fact]
    public void A_weekly_window_starts_on_Monday_and_is_dated_by_its_start()
    {
        var clause = InfluxService.AggregateWindowClause(InfluxService.WeeklyWindow, "max");

        Assert.Equal("|> aggregateWindow(every: 1w, offset: -3d, timeSrc: \"_start\", fn: max, createEmpty: false)", clause);
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
