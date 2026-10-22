using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The alarm thresholds of every channel per sensor (dashboard redesign, slice s2), from the "Seuil Alarme" rows as SQL
/// returns them: one parse loop for the dashboard's curves and for the single-channel map the Surveillance tab and the
/// legacy copy have always read (<see cref="SensorChargeRatioHelper.ForChannel"/>). A later row's non-zero value wins
/// per channel, a zero is no threshold, a blank value is nothing at all, an unreadable part is reported once per row.
/// </summary>
public class ThresholdsByChannelTests
{
    static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    static Dictionary<Guid, Dictionary<string, double>> Read(params (Guid ElementId, string? Value)[] rows) =>
        SensorChargeRatioHelper.ThresholdsByChannel(rows);

    [Fact]
    public void Every_channel_of_a_threshold_is_kept_per_sensor()
    {
        var byChannel = Read((A, "weight:1500;temperature:60"));

        Assert.Equal(new[] { A }, byChannel.Keys);
        Assert.Equal(1500, byChannel[A]["weight"]);
        Assert.Equal(60, byChannel[A]["temperature"]);
        Assert.Equal(2, byChannel[A].Count);
    }

    [Fact]
    public void A_zero_threshold_is_no_threshold_and_replaces_no_earlier_one()
    {
        Assert.Equal(1500, Read((A, "weight:1500"), (A, "weight:0"))[A]["weight"]);
        Assert.Empty(Read((B, "weight:0")));                                  // a zero alone: no threshold, no entry
    }

    [Fact]
    public void A_later_attribute_row_wins_per_channel()
    {
        // The known attribute duplicates: two "Seuil Alarme" rows on one sensor.
        var byChannel = Read((A, "weight:1500;temperature:60"), (A, "weight:2000"));

        Assert.Equal(2000, byChannel[A]["weight"]);
        Assert.Equal(60, byChannel[A]["temperature"]);
    }

    [Fact]
    public void Channel_names_match_whatever_their_case()
    {
        var byChannel = Read((A, "Extension:2"));

        Assert.Equal(2, byChannel[A]["extension"]);
        Assert.Equal(2, byChannel[A]["EXTENSION"]);
    }

    [Fact]
    public void An_unreadable_part_is_reported_once_per_row_and_the_readable_parts_still_count()
    {
        var unreadable = new List<(Guid ElementId, string Value)>();
        var byChannel = SensorChargeRatioHelper.ThresholdsByChannel(new (Guid, string?)[] { (A, "extension:2;2;bad") }, unreadable);

        Assert.Equal(new[] { (A, "extension:2;2;bad") }, unreadable);
        Assert.Equal(2, byChannel[A]["extension"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_is_no_threshold_and_not_unreadable(string? value)
    {
        var unreadable = new List<(Guid ElementId, string Value)>();
        var byChannel = SensorChargeRatioHelper.ThresholdsByChannel(new (Guid, string?)[] { (A, value) }, unreadable);

        Assert.Empty(byChannel);
        Assert.Empty(unreadable);
    }

    [Fact]
    public void One_channel_s_map_is_what_LoadThresholdsAsync_always_returned()
    {
        var byChannel = Read((A, "Weight:1500;temperature:60"), (B, "temperature:50"));

        foreach (var asked in new[] { "weight", " WEIGHT " })
        {
            var weight = SensorChargeRatioHelper.ForChannel(byChannel, asked);
            Assert.Equal(new[] { A }, weight.Keys);                          // B has no weight threshold: absent
            Assert.Equal(1500, weight[A]);
        }
        Assert.Equal(new[] { A, B }, SensorChargeRatioHelper.ForChannel(byChannel, "temperature").Keys.OrderBy(k => k));
        Assert.Empty(SensorChargeRatioHelper.ForChannel(byChannel, "extension"));
    }
}
