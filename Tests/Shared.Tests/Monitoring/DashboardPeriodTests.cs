using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's one period (decision 1 of the 2026-10-22 redesign): five spans, one month by default, each counted
/// back from one "now"; an unknown code throws instead of quietly meaning "since 2020-01-01".
/// </summary>
public class DashboardPeriodTests
{
    static readonly DateTime Now = new(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_dashboard_offers_exactly_five_periods_in_order_with_one_month_by_default()
    {
        Assert.Equal(new[] { "1m", "3m", "6m", "1y", "5y" }, DashboardPeriod.All.Select(p => p.Code));
        Assert.Equal(new[] { "1 mois", "3 mois", "6 mois", "1 an", "5 ans" }, DashboardPeriod.All.Select(p => p.Label));
        Assert.Same(DashboardPeriod.All[0], DashboardPeriod.Default);
        Assert.Equal("1 mois", DashboardPeriod.Default.Label);
    }

    [Theory]
    [InlineData("1m", "2026-05-17T12:00:00Z")]
    [InlineData("3m", "2026-03-17T12:00:00Z")]
    [InlineData("6m", "2025-12-17T12:00:00Z")]
    [InlineData("1y", "2025-06-17T12:00:00Z")]
    [InlineData("5y", "2021-06-17T12:00:00Z")]
    public void Each_period_spans_its_months_back_from_now(string code, string start)
    {
        var (from, to) = DashboardPeriod.Of(code).Span(Now);

        Assert.Equal(DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal), from);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(Now, to);
    }

    [Fact]
    public void A_month_back_from_a_month_end_or_a_leap_day_lands_on_the_last_day_of_the_shorter_month()
    {
        var monthEnd = new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 2, 28, 10, 0, 0, DateTimeKind.Utc), DashboardPeriod.Of("1m").Span(monthEnd).Start);

        var leapDay = new DateTime(2028, 2, 29, 8, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2023, 2, 28, 8, 30, 0, DateTimeKind.Utc), DashboardPeriod.Of("5y").Span(leapDay).Start);
    }

    [Theory]
    [InlineData("1w")]
    [InlineData("10y")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_period_code_throws_and_names_it(string? code)
    {
        var ex = Assert.Throws<ArgumentException>(() => DashboardPeriod.Of(code));
        Assert.Contains(code is null ? "null" : $"\"{code}\"", ex.Message);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void A_span_refuses_a_now_that_is_not_utc(DateTimeKind kind)
    {
        var notUtc = DateTime.SpecifyKind(Now, kind);
        Assert.Throws<ArgumentException>(() => DashboardPeriod.Default.Span(notUtc));
    }
}
