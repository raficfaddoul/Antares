using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The one rule behind the dashboard's gauges, the plan's colours and the weekly peaks: a reading is rated against
/// its own sensor's alarm threshold, and a sensor with no threshold gets no ratio at all, never an assumed one.
/// </summary>
public class ChargeRatioTests
{
    static readonly Guid Rated = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid ZeroThreshold = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid NoThreshold = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    static readonly Dictionary<Guid, double> Thresholds = new() { [Rated] = 1500, [ZeroThreshold] = 0 };

    [Fact]
    public void A_reading_is_rated_against_its_own_threshold()
    {
        Assert.Equal(1.2, SensorChargeRatioHelper.Ratio(Thresholds, Rated, 1800)!.Value, 10);
    }

    [Theory]
    [InlineData("cccccccc-0000-0000-0000-000000000003")]   // no threshold
    [InlineData("bbbbbbbb-0000-0000-0000-000000000002")]   // a zero threshold rates nothing
    public void A_sensor_without_a_usable_threshold_gets_no_ratio(string sensor)
    {
        Assert.Null(SensorChargeRatioHelper.Ratio(Thresholds, Guid.Parse(sensor), 1800));
    }
}
