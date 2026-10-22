using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's shackle figures (decisions 3, 4, 6 and 11 of the 2026-10-22 redesign), from the rows InfluxService
/// returns: each shackle rated against its own threshold, the real value above 100 %, the critical share over a
/// named 0.80, nearest-percent rounding, no figure at all rather than zeros when nothing was measured, and the max
/// gauge, the highest weekly peak and the worst plan colour one number by construction.
/// </summary>
public class ShackleChargeTests
{
    static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid Unrated = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    static readonly DateTime Week1 = new(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);   // Mondays
    static readonly DateTime Week2 = new(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Week3 = new(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);

    // An InfluxService.GetData row: [0] time, [1] sensor id, [2] the channel's value.
    static object?[] Row(DateTime at, Guid sensor, double? value) => new object?[] { at, sensor.ToString(), value };

    static ShackleCharge Charge(Guid[] shackles, Dictionary<Guid, double> thresholds, Dictionary<Guid, double> means,
        params object?[][] weeklyMaxRows) =>
        SensorChargeRatioHelper.Charge(shackles, thresholds, means, weeklyMaxRows!);

    [Fact]
    public void A_shackle_at_1_4_times_its_threshold_reads_140_percent()
    {
        var charge = Charge(new[] { A }, new() { [A] = 1500 }, new() { [A] = 750 }, Row(Week1, A, 2100));

        Assert.Equal(140, charge.Gauges!.MaxPercent);
        Assert.Equal(1.4, charge.MaxRatioBySensor[A], 10);
        Assert.Equal(100, charge.Gauges.CriticalPercent);
    }

    [Fact]
    public void Percentages_round_to_the_nearest_whole_percent()
    {
        Assert.Equal(29, SensorChargeRatioHelper.Percent(0.29));          // truncation gave 28
        Assert.Equal(67, SensorChargeRatioHelper.Percent(2.0 / 3));
        Assert.Equal(13, SensorChargeRatioHelper.Percent(0.125));         // a midpoint goes away from zero, not to the even 12

        var charge = Charge(new[] { A }, new() { [A] = 1000 }, new() { [A] = 290 }, Row(Week1, A, 290));
        Assert.Equal(new ShackleGauges(29, 29, 0), charge.Gauges);
    }

    [Fact]
    public void Exactly_0_80_is_not_critical_and_just_above_is()
    {
        Assert.Equal(0.80, SensorChargeRatioHelper.CriticalRatio);
        var thresholds = new Dictionary<Guid, double> { [A] = 1000, [B] = 1000 };
        var means = new Dictionary<Guid, double> { [A] = 100, [B] = 100 };

        var atTheLine = Charge(new[] { A, B }, thresholds, means, Row(Week1, A, 800), Row(Week1, B, 100));
        Assert.Equal(0, atTheLine.Gauges!.CriticalPercent);

        var justAbove = Charge(new[] { A, B }, thresholds, means, Row(Week1, A, 801), Row(Week1, B, 100));
        Assert.Equal(50, justAbove.Gauges!.CriticalPercent);
    }

    [Fact]
    public void A_shackle_without_a_threshold_is_left_out_and_counted()
    {
        var charge = Charge(new[] { A, Unrated }, new() { [A] = 1000 }, new() { [A] = 400, [Unrated] = 5000 },
            Row(Week1, A, 500), Row(Week1, Unrated, 9000), Row(Week2, Unrated, 9000));

        Assert.Equal(new ShackleGauges(40, 50, 0), charge.Gauges);
        Assert.Equal(new[] { A }, charge.MaxRatioBySensor.Keys);
        Assert.All(charge.WeeklyPeaks, p => Assert.Equal(A, p.SensorId));
        Assert.Single(charge.WeeklyPeaks);
        Assert.Equal(1, charge.Rated);
        Assert.Equal(1, charge.WithoutThreshold);
    }

    [Fact]
    public void No_reading_of_a_rated_shackle_gives_no_gauges_never_zeros()
    {
        var charge = Charge(new[] { A, B }, new() { [A] = 1000 }, new());

        Assert.Null(charge.Gauges);
        Assert.Empty(charge.MaxRatioBySensor);
        Assert.Empty(charge.WeeklyPeaks);
        Assert.Equal(1, charge.Rated);
        Assert.Equal(1, charge.WithoutThreshold);
    }

    [Fact]
    public void The_mean_gauge_averages_each_shackle_s_mean_over_its_own_threshold()
    {
        var charge = Charge(new[] { A, B }, new() { [A] = 1000, [B] = 2000 }, new() { [A] = 500, [B] = 1600 },
            Row(Week1, A, 600), Row(Week1, B, 1700));

        Assert.Equal(65, charge.Gauges!.MeanPercent);                    // (0.5 + 0.8) / 2, not 2100 / 3000
    }

    [Fact]
    public void The_max_gauge_equals_the_highest_weekly_peak_and_the_worst_plan_ratio()
    {
        var charge = Charge(new[] { A, B }, new() { [A] = 1000, [B] = 1500 }, new() { [A] = 300, [B] = 300 },
            Row(Week1, A, 300), Row(Week1, B, 1200), Row(Week2, A, 1130), Row(Week2, B, 1500), Row(Week3, A, 200), Row(Week3, B, null));

        Assert.Equal(new[] { 0.8, 1.13, 0.2 }, charge.WeeklyPeaks.Select(p => Math.Round(p.Ratio, 10)));
        Assert.Equal(new[] { B, A, A }, charge.WeeklyPeaks.Select(p => p.SensorId));
        Assert.Equal(1.13, charge.MaxRatioBySensor[A], 10);
        Assert.Equal(1.0, charge.MaxRatioBySensor[B], 10);
        Assert.Equal(113, charge.Gauges!.MaxPercent);
        Assert.Equal(SensorChargeRatioHelper.Percent(charge.WeeklyPeaks.Max(p => p.Ratio)), charge.Gauges.MaxPercent);
        Assert.Equal(SensorChargeRatioHelper.Percent(charge.MaxRatioBySensor.Values.Max()), charge.Gauges.MaxPercent);
    }

    [Fact]
    public void Rows_after_a_threshold_less_sensor_colour_nothing()
    {
        var charge = Charge(new[] { A, Unrated }, new() { [A] = 1000 }, new() { [Unrated] = 900 },
            Row(Week1, Unrated, 900), Row(Week2, Unrated, 950));

        Assert.Empty(charge.MaxRatioBySensor);
        Assert.Empty(charge.WeeklyPeaks);
        Assert.Null(charge.Gauges);
        Assert.Equal(1, charge.Rated);
    }
}
