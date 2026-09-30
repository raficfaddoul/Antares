using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The packed "Seuil Alarme" value, "weight:1500;temperature:60", as the dashboard, the plan's colours and the
/// Charpente Element Mapping page read it. What matters: a part that is not "channel:number" counts for no channel
/// and is reported, so a bare "1500" can no longer take a shackle out of the gauges without a word.
/// </summary>
public class AlarmThresholdFormatTests
{
    [Fact]
    public void Each_channel_gets_its_own_threshold()
    {
        var unreadable = new List<string>();

        var thresholds = AlarmThresholdFormat.Parse("weight:1500;temperature:60", unreadable);

        Assert.Equal(2, thresholds.Count);
        Assert.Equal(1500, thresholds["weight"]);
        Assert.Equal(60, thresholds["temperature"]);
        Assert.Empty(unreadable);
    }

    [Fact]
    public void A_bare_number_is_no_threshold_and_is_reported()
    {
        var unreadable = new List<string>();

        var thresholds = AlarmThresholdFormat.Parse("1500", unreadable);

        Assert.Empty(thresholds);
        Assert.Equal(new[] { "1500" }, unreadable);
    }

    [Fact]
    public void A_readable_part_still_counts_beside_an_unreadable_one()
    {
        var unreadable = new List<string>();

        var thresholds = AlarmThresholdFormat.Parse("weight:1500; temperature:6O ;1500", unreadable);

        Assert.Equal(1500, thresholds["weight"]);
        Assert.False(thresholds.ContainsKey("temperature"));
        Assert.Equal(new[] { "temperature:6O", "1500" }, unreadable);
    }

    [Theory]
    [InlineData("weight:1,5")]      // a decimal comma: the value is read in the invariant culture
    [InlineData(":1500")]           // no channel
    [InlineData("weight:")]         // no number
    [InlineData("weight:1500:kg")]  // one colon too many
    public void Malformed_parts_are_reported(string value)
    {
        var unreadable = new List<string>();

        var thresholds = AlarmThresholdFormat.Parse(value, unreadable);

        Assert.Empty(thresholds);
        Assert.Equal(new[] { value }, unreadable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("weight:1500;")]
    [InlineData("weight:1500; ")]
    public void Empty_values_and_trailing_separators_are_not_mistakes(string value)
    {
        var unreadable = new List<string>();

        AlarmThresholdFormat.Parse(value, unreadable);

        Assert.Empty(unreadable);
    }

    [Fact]
    public void Channel_names_ignore_case_and_spaces_and_the_last_one_given_wins()
    {
        var thresholds = AlarmThresholdFormat.Parse(" Weight : 1500 ;weight:2000");

        Assert.Single(thresholds);
        Assert.Equal(2000, thresholds["WEIGHT"]);
    }

    [Fact]
    public void The_rewritten_shackle_value_reads_as_a_weight_threshold()
    {
        // The form the 2026-09-16 startup patch writes, for a blank value and for a bare number alike.
        var unreadable = new List<string>();

        var thresholds = AlarmThresholdFormat.Parse("weight:1500", unreadable);

        Assert.Equal(1500, thresholds["weight"]);
        Assert.Empty(unreadable);
    }
}
