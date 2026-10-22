using AliantWebS.Data;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// Which Y axis each curve of a sensor plot goes on. What matters: temperature is on the left whatever else is
/// plotted and in whatever order, every other channel is on the right, no axis is left empty, and several other
/// channels sharing one scale is said, never silent.
/// </summary>
public class PlotAxesTests
{
    [Fact]
    public void An_inclinometer_has_its_temperature_on_the_left_and_both_tilts_on_the_right()
    {
        var axes = PlotAxes.For(new[]
        {
            ("I1|temperature", "temperature"),
            ("I1|x_axis", "x_axis"),
            ("I1|y_axis", "y_axis"),
        });

        Assert.Equal(PlotAxes.Left, axes.AxisOfSeries["I1|temperature"]);
        Assert.Equal(PlotAxes.Right, axes.AxisOfSeries["I1|x_axis"]);
        Assert.Equal(PlotAxes.Right, axes.AxisOfSeries["I1|y_axis"]);
        Assert.Equal("temperature", axes.LeftLabel);
        Assert.Equal("x_axis, y_axis", axes.RightLabel);
    }

    [Fact]
    public void Several_channels_on_one_axis_are_said_in_a_notice_naming_the_axis_and_the_channels()
    {
        var axes = PlotAxes.For(new[]
        {
            ("I1|y_axis", "y_axis"),
            ("I1|temperature", "temperature"),
            ("I1|x_axis", "x_axis"),
        });

        Assert.Equal("Axe de droite partagé par x_axis, y_axis : une seule échelle pour ces canaux.", axes.Notice);
    }

    [Fact]
    public void A_shackle_has_its_temperature_on_the_left_and_its_weight_on_the_right_without_a_notice()
    {
        var axes = PlotAxes.For(new[] { ("M1|weight", "weight"), ("M1|temperature", "temperature") });

        Assert.Equal(PlotAxes.Left, axes.AxisOfSeries["M1|temperature"]);
        Assert.Equal(PlotAxes.Right, axes.AxisOfSeries["M1|weight"]);
        Assert.Equal("temperature", axes.LeftLabel);
        Assert.Equal("weight", axes.RightLabel);
        Assert.Null(axes.Notice);
    }

    [Theory]
    // Before 2026-10-22 the first name in alphabetical order took the left axis: these put temperature on the right.
    [InlineData("extension")]
    [InlineData("humidity")]
    [InlineData("strain")]
    [InlineData("weight")]
    public void Temperature_is_on_the_left_whatever_the_other_channel_is_called(string other)
    {
        var axes = PlotAxes.For(new[] { ($"S|{other}", other), ("S|temperature", "temperature") });

        Assert.Equal(PlotAxes.Left, axes.AxisOfSeries["S|temperature"]);
        Assert.Equal(PlotAxes.Right, axes.AxisOfSeries[$"S|{other}"]);
    }

    [Fact]
    public void Every_temperature_channel_shares_the_left_axis_without_a_notice()
    {
        // A hygrometer: two temperatures, both in degrees Celsius, and the humidity.
        var axes = PlotAxes.For(new[]
        {
            ("H|internal_temperature", "internal_temperature"),
            ("H|humidity", "humidity"),
            ("H|external_temperature", "external_temperature"),
        });

        Assert.Equal(PlotAxes.Left, axes.AxisOfSeries["H|internal_temperature"]);
        Assert.Equal(PlotAxes.Left, axes.AxisOfSeries["H|external_temperature"]);
        Assert.Equal(PlotAxes.Right, axes.AxisOfSeries["H|humidity"]);
        Assert.Equal("external_temperature, internal_temperature", axes.LeftLabel);
        Assert.Equal("humidity", axes.RightLabel);
        Assert.Null(axes.Notice);
    }

    [Fact]
    public void A_channel_is_a_temperature_whatever_its_case()
    {
        Assert.True(PlotAxes.IsTemperature("Temperature"));
        Assert.True(PlotAxes.IsTemperature("external_TEMPERATURE"));
        Assert.False(PlotAxes.IsTemperature("weight"));
    }

    [Fact]
    public void Temperatures_alone_use_the_left_axis_only()
    {
        var axes = PlotAxes.For(new[] { ("M1|temperature", "temperature"), ("M2|temperature", "temperature") });

        Assert.All(axes.AxisOfSeries.Values, axis => Assert.Equal(PlotAxes.Left, axis));
        Assert.Equal("temperature", axes.LeftLabel);
        Assert.Equal("", axes.RightLabel);
        Assert.Null(axes.Notice);
    }

    [Fact]
    public void Without_temperature_the_other_channel_takes_the_left_axis_rather_than_leave_it_empty()
    {
        var axes = PlotAxes.For(new[] { ("M1|weight", "weight"), ("M2|weight", "weight") });

        Assert.All(axes.AxisOfSeries.Values, axis => Assert.Equal(PlotAxes.Left, axis));
        Assert.Equal("weight", axes.LeftLabel);
        Assert.Equal("", axes.RightLabel);
        Assert.Null(axes.Notice);
    }

    [Fact]
    public void Without_temperature_several_channels_share_the_left_axis_and_the_notice_says_so()
    {
        var axes = PlotAxes.For(new[]
        {
            ("A|Vertical Acceleration", "Vertical Acceleration"),
            ("A|Horizontal Acceleration 1", "Horizontal Acceleration 1"),
        });

        Assert.All(axes.AxisOfSeries.Values, axis => Assert.Equal(PlotAxes.Left, axis));
        Assert.Equal("Horizontal Acceleration 1, Vertical Acceleration", axes.LeftLabel);
        Assert.Equal(
            "Axe de gauche partagé par Horizontal Acceleration 1, Vertical Acceleration : une seule échelle pour ces canaux.",
            axes.Notice);
    }

    [Fact]
    public void Nothing_plotted_gives_no_axis_label_and_no_notice()
    {
        var axes = PlotAxes.For(Array.Empty<(string, string)>());

        Assert.Empty(axes.AxisOfSeries);
        Assert.Equal("", axes.LeftLabel);
        Assert.Equal("", axes.RightLabel);
        Assert.Null(axes.Notice);
    }

    [Fact]
    public void A_curve_given_twice_on_one_axis_is_one_curve()
    {
        // Two sensors with the same name give the same header; dygraph keys its series options by header.
        var axes = PlotAxes.For(new[] { ("M|weight", "weight"), ("M|weight", "weight") });

        Assert.Equal(PlotAxes.Left, Assert.Single(axes.AxisOfSeries).Value);
    }

    [Fact]
    public void A_curve_that_would_need_both_axes_is_refused()
    {
        var curves = new[] { ("M|channel", "temperature"), ("M|channel", "weight") };

        var refused = Assert.Throws<ArgumentException>(() => PlotAxes.For(curves));
        Assert.Contains("M|channel", refused.Message);
    }

    [Fact]
    public void A_curve_without_a_channel_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PlotAxes.For(new[] { ("M|?", (string)null!) }));
    }
}
