using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's "Charge max manilles / seuil d'alarme" curve and the shackle named when the pointer is on one
/// of its points. What matters: each week's point is the highest reading rated against ITS OWN sensor's threshold,
/// it names the sensor that gave it, and a week runs Monday to Sunday.
/// </summary>
public class WeeklyPeaksTests
{
    static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid NoThreshold = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    static readonly Dictionary<Guid, double> Thresholds = new() { [A] = 1500, [B] = 1000 };

    // A row as InfluxService.GetData returns it: [0] time, [1] sensor id, [2] value.
    static object[] Row(DateTime at, Guid sensor, double? value) => new object[] { at, sensor.ToString(), value };

    static DateTime U(int month, int day, int h = 12, int m = 0, int s = 0) => new(2026, month, day, h, m, s, DateTimeKind.Utc);

    [Fact]
    public void The_peak_is_rated_against_its_own_sensors_threshold_and_names_that_sensor()
    {
        // A carries more kilograms, but B is closer to its own threshold: 900/1000 beats 1200/1500.
        var peaks = SensorChargeRatioHelper.WeeklyPeaks(new[]
        {
            Row(U(9, 29), A, 1200),
            Row(U(9, 30), B, 900),
        }, Thresholds);

        var peak = Assert.Single(peaks);
        Assert.Equal(U(9, 28, 0), peak.Week);
        Assert.Equal(B, peak.SensorId);
        Assert.Equal(0.9, peak.Ratio, 10);
    }

    [Fact]
    public void A_week_runs_Monday_to_Sunday_and_is_dated_by_its_Monday()
    {
        var peaks = SensorChargeRatioHelper.WeeklyPeaks(new[]
        {
            Row(U(9, 28, 0, 0, 0), A, 300),      // Monday 00:00:00
            Row(U(10, 4, 23, 59, 59), B, 500),   // Sunday 23:59:59, same week
            Row(U(10, 5, 0, 0, 0), A, 1400),     // Monday 00:00:00, next week
        }, Thresholds);

        Assert.Equal(new[] { U(9, 28, 0), U(10, 5, 0) }, peaks.Select(p => p.Week));
        Assert.Equal(new[] { B, A }, peaks.Select(p => p.SensorId));
    }

    [Fact]
    public void Readings_without_a_threshold_or_a_value_are_left_out()
    {
        var peaks = SensorChargeRatioHelper.WeeklyPeaks(new[]
        {
            Row(U(9, 29), NoThreshold, 99999),   // no threshold: nothing is assumed for it
            Row(U(9, 30), B, null),              // no value
            Row(U(10, 1), A, 150),
            Row(U(10, 6), NoThreshold, 5000),    // the only reading of the next week
        }, Thresholds);

        var peak = Assert.Single(peaks);
        Assert.Equal(A, peak.SensorId);
        Assert.Equal(0.1, peak.Ratio, 10);
    }

    [Fact]
    public void On_a_tie_the_first_reading_keeps_the_peak()
    {
        var peaks = SensorChargeRatioHelper.WeeklyPeaks(new[]
        {
            Row(U(9, 29), A, 1500),   // 1.0
            Row(U(9, 30), B, 1000),   // 1.0
        }, Thresholds);

        Assert.Equal(A, Assert.Single(peaks).SensorId);
    }
}
