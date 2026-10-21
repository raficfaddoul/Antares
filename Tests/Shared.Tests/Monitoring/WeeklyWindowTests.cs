using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's weekly graphs (shackle load, inclination, the third curve) take one maximum per week from
/// InfluxDB. What matters: a week is Monday to Sunday and its row carries that Monday, so a peak sits under the
/// week it happened in. Plain "1w" windows run Thursday to Wednesday and carry the following Thursday.
/// </summary>
public class WeeklyWindowTests
{
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
