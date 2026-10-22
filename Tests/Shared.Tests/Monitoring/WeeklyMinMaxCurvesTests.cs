using AliantWebS.Data;
using AliantWebS.Models;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's bottom-right graph (decision 12 of the 2026-10-22 redesign, slice s3): for a sensor type, the weekly
/// minimum AND maximum of every channel across the type's sensors, placed on the two axes by PlotAxes. A wind direction
/// is left out (a weekly min or max of an angle means nothing); a channel without a reading or without a recorded unit
/// is listed so the page can say it; no unit is ever put in a name. Whatever would mislabel the graph (a comma, two
/// channels of one name) or hide a broken read (a malformed row, a type given without its channels, nothing to read) is
/// refused, never skipped.
/// </summary>
public class WeeklyMinMaxCurvesTests
{
    static readonly DateTime Week1 = new(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);   // Mondays
    static readonly DateTime Week2 = new(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);

    static ChannelDataType Channel(string name, string? units = null) =>
        new() { ChannelDataTypeId = Guid.NewGuid(), Name = name, Units = units };

    static ElementType Type(string name, params ChannelDataType[] channels)
    {
        var type = new ElementType { ElementTypeId = Guid.NewGuid(), Name = name };
        foreach (var c in channels)
            type.ChannelDataTypeElementTypes.Add(new ChannelDataTypeElementType
            {
                ChannelDataTypeElementTypeId = Guid.NewGuid(), ElementTypeId = type.ElementTypeId,
                ChannelDataTypeId = c.ChannelDataTypeId, ChannelDataType = c, ElementType = type,
            });
        return type;
    }

    // An InfluxService.GetData row: [0] time, [1] sensor id, [2..] the plotted channels in order (null = no reading).
    static object[] Row(DateTime at, string sensor, params double?[] values) =>
        values.Select(v => (object?)v).Prepend(sensor).Prepend(at).ToArray()!;

    static object[][] Rows(params object[][] rows) => rows;
    static readonly object[][] None = Array.Empty<object[]>();

    static WeeklyMinMax Build(IReadOnlyList<ChannelDataType> plotted, object[][] minRows, object[][] maxRows) =>
        WeeklyMinMaxCurves.Build(plotted, minRows, maxRows);

    static double At(WeeklyMinMax curve, string series, DateTime week) =>
        curve.Indices[curve.Headers.IndexOf(series) - 1][week];

    // ── Series ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_sensors_in_one_week_give_the_lowest_min_and_the_highest_max()
    {
        var x = Channel("x_axis");
        var curve = Build(new[] { x },
            Rows(Row(Week1, "I1", -0.4), Row(Week1, "I2", 0.1)),
            Rows(Row(Week1, "I1", 0.3), Row(Week1, "I2", 0.1)));

        Assert.Equal(-0.4, At(curve, "x_axis (min)", Week1));            // negative kept, never clamped nor made absolute
        Assert.Equal(0.3, At(curve, "x_axis (max)", Week1));
    }

    [Fact]
    public void A_row_dated_by_a_mid_week_range_start_is_filed_under_its_Monday()
    {
        var x = Channel("x_axis");
        var sunday = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);   // 1 mois back from 2026-06-17 12:00
        var curve = Build(new[] { x }, Rows(Row(sunday, "I1", 0.2)), Rows(Row(sunday, "I1", 0.5)));

        Assert.Equal(new object[] { Week1 }, curve.Indices[0].Keys);
        Assert.Equal(new object[] { Week1 }, curve.Indices[1].Keys);
    }

    [Fact]
    public void Series_come_in_channel_name_order_min_before_max_whatever_the_link_order()
    {
        var y = Channel("y_axis"); var t = Channel("temperature"); var x = Channel("x_axis");
        var channels = WeeklyMinMaxCurves.ChannelsOf(Type("Inclinomètre", y, t, x));
        Assert.Equal(new[] { t, x, y }, channels.Plotted);

        var row = Row(Week1, "I1", 12, 0.1, 0.2);
        var curve = Build(channels.Plotted, Rows(row), Rows(row));

        Assert.Equal(new[]
        {
            "Date", "temperature (min)", "temperature (max)", "x_axis (min)", "x_axis (max)", "y_axis (min)", "y_axis (max)",
        }, curve.Headers);
        Assert.Equal(curve.Headers.Count - 1, curve.Indices.Count);
        Assert.Equal(0.2, At(curve, "y_axis (max)", Week1));
    }

    [Fact]
    public void Min_comes_from_the_min_rows_and_max_from_the_max_rows()
    {
        var x = Channel("x_axis");
        var curve = Build(new[] { x },
            Rows(Row(Week1, "I1", 0.1)),
            Rows(Row(Week1, "I1", 0.9), Row(Week2, "I1", 0.7)));

        Assert.Equal(new object[] { Week1 }, curve.Indices[0].Keys);            // no min for week 2: none was read
        Assert.Equal(new object[] { Week1, Week2 }, curve.Indices[1].Keys);
        Assert.Equal(0.1, At(curve, "x_axis (min)", Week1));
        Assert.Equal(0.7, At(curve, "x_axis (max)", Week2));
    }

    [Fact]
    public void A_series_with_a_value_only_in_the_max_rows_is_drawn_alone()
    {
        var x = Channel("x_axis");
        var curve = Build(new[] { x }, Rows(Row(Week1, "I1", (double?)null)), Rows(Row(Week1, "I1", 0.4)));

        Assert.Equal(new[] { "Date", "x_axis (max)" }, curve.Headers);
        Assert.Empty(curve.WithoutReading);
    }

    [Fact]
    public void A_channel_without_a_reading_has_no_series_and_is_listed()
    {
        var t = Channel("temperature"); var x = Channel("x_axis"); var y = Channel("y_axis");
        var row = Row(Week1, "I1", null, 0.3, null);
        var curve = Build(new[] { t, x, y }, Rows(row), Rows(row));

        Assert.Equal(new[] { "Date", "x_axis (min)", "x_axis (max)" }, curve.Headers);
        Assert.Equal(new[] { "temperature", "y_axis" }, curve.WithoutReading);
        Assert.Equal(new[] { "x_axis" }, curve.WithoutUnit);                    // only drawn channels
        Assert.True(curve.HasSeries);
    }

    [Fact]
    public void Nothing_read_gives_no_series_no_label_no_notice_and_no_unit_line()
    {
        var t = Channel("temperature"); var x = Channel("x_axis");
        var curve = Build(new[] { t, x }, None, None);

        Assert.False(curve.HasSeries);
        Assert.Equal(new[] { "Date" }, curve.Headers);
        Assert.Empty(curve.Indices);
        Assert.Empty(curve.Axes.AxisOfSeries);
        Assert.Equal("", curve.Axes.LeftLabel);
        Assert.Equal("", curve.Axes.RightLabel);
        Assert.Null(curve.Axes.Notice);
        Assert.Empty(curve.WithoutUnit);
        Assert.Equal(new[] { "temperature", "x_axis" }, curve.WithoutReading);
    }

    [Fact]
    public void Four_decimals_are_kept()
    {
        var x = Channel("x_axis");
        var curve = Build(new[] { x }, Rows(Row(Week1, "I1", 0.123456)), Rows(Row(Week1, "I1", -0.987654)));

        Assert.Equal(0.1235, At(curve, "x_axis (min)", Week1));
        Assert.Equal(-0.9877, At(curve, "x_axis (max)", Week1));
        Assert.Equal(4, WeeklyMinMaxCurves.Decimals);
    }

    // ── Direction ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("direction")]
    [InlineData("Direction")]
    [InlineData("DIRECTION")]
    public void A_direction_channel_is_left_out_whatever_its_case(string name)
    {
        var speed = Channel("speed"); var direction = Channel(name);
        var channels = WeeklyMinMaxCurves.ChannelsOf(Type("wind_station", speed, direction));

        Assert.Equal(new[] { speed }, channels.Plotted);
        Assert.Equal(new[] { direction }, channels.LeftOut);
        Assert.True(WeeklyMinMaxCurves.IsDirection(name));
    }

    [Theory]
    [InlineData("speed")]
    [InlineData("x_axis")]
    [InlineData("temperature")]
    [InlineData("direction_x")]
    public void Other_channels_are_not_directions(string name) => Assert.False(WeeklyMinMaxCurves.IsDirection(name));

    [Fact]
    public void A_type_with_only_a_direction_has_nothing_to_plot()
    {
        var direction = Channel("direction");
        var channels = WeeklyMinMaxCurves.ChannelsOf(Type("Girouette", direction));

        Assert.Empty(channels.Plotted);
        Assert.Equal(new[] { direction }, channels.LeftOut);
    }

    [Fact]
    public void A_channel_linked_twice_is_one_channel()
    {
        var x = Channel("x_axis");
        var type = Type("Inclinomètre", x, x);

        Assert.Equal(new[] { x }, WeeklyMinMaxCurves.ChannelsOf(type).Plotted);
    }

    // ── Units ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Without_unit_lists_null_and_blank_units_of_drawn_channels_only()
    {
        var a = Channel("a", null); var b = Channel("b", ""); var c = Channel("c", "  "); var d = Channel("d", "mm/m");
        var e = Channel("e", null);   // no reading: said as such, not as unitless
        var row = Row(Week1, "S1", 1, 2, 3, 4, null);
        var curve = Build(new[] { a, b, c, d, e }, Rows(row), Rows(row));

        Assert.Equal(new[] { "a", "b", "c" }, curve.WithoutUnit);
        Assert.Equal(new[] { "e" }, curve.WithoutReading);
    }

    [Fact]
    public void No_unit_ever_appears_in_a_header_or_a_label()
    {
        var t = Channel("temperature", "°C"); var x = Channel("x_axis", "mm/m");
        var row = Row(Week1, "I1", 12, 0.3);
        var curve = Build(new[] { t, x }, Rows(row), Rows(row));

        foreach (var text in curve.Headers.Append(curve.Axes.LeftLabel).Append(curve.Axes.RightLabel))
        {
            Assert.DoesNotContain("°C", text);
            Assert.DoesNotContain("mm/m", text);
        }
        Assert.Equal("temperature", curve.Axes.LeftLabel);
        Assert.Equal("x_axis", curve.Axes.RightLabel);
        Assert.Empty(curve.WithoutUnit);
    }

    // ── Axes: PlotAxes over the drawn series ───────────────────────────────────────────────────────────────────

    static WeeklyMinMax Drawn(params ChannelDataType[] channels)
    {
        var row = Row(Week1, "S1", channels.Select((_, i) => (double?)(i + 1)).ToArray());
        return Build(channels, Rows(row), Rows(row));
    }

    static IEnumerable<(string Series, string Channel)> Pairs(params string[] channels) =>
        channels.SelectMany(c => new[] { ($"{c} (min)", c), ($"{c} (max)", c) });

    [Fact]
    public void An_inclinometer_has_its_temperature_on_the_left_and_its_tilts_on_a_shared_right_axis()
    {
        var curve = Drawn(Channel("temperature"), Channel("x_axis"), Channel("y_axis"));

        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["temperature (min)"]);
        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["temperature (max)"]);
        foreach (var s in new[] { "x_axis (min)", "x_axis (max)", "y_axis (min)", "y_axis (max)" })
            Assert.Equal(PlotAxes.Right, curve.Axes.AxisOfSeries[s]);
        Assert.Equal("temperature", curve.Axes.LeftLabel);
        Assert.Equal("x_axis, y_axis", curve.Axes.RightLabel);
        Assert.Equal("Axe de droite partagé par x_axis, y_axis : une seule échelle pour ces canaux.", curve.Axes.Notice);
    }

    [Fact]
    public void A_shackle_has_its_temperature_on_the_left_and_its_weight_on_the_right_without_a_notice()
    {
        var curve = Drawn(Channel("temperature"), Channel("weight"));

        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["temperature (max)"]);
        Assert.Equal(PlotAxes.Right, curve.Axes.AxisOfSeries["weight (min)"]);
        Assert.Equal(PlotAxes.Right, curve.Axes.AxisOfSeries["weight (max)"]);
        Assert.Null(curve.Axes.Notice);
    }

    [Fact]
    public void A_hygrometer_has_both_temperatures_on_the_left_and_PlotAxes_own_notice()
    {
        var curve = Drawn(Channel("external_temperature"), Channel("humidity"), Channel("internal_temperature"));

        foreach (var s in new[] { "external_temperature (min)", "external_temperature (max)", "internal_temperature (min)", "internal_temperature (max)" })
            Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries[s]);
        Assert.Equal(PlotAxes.Right, curve.Axes.AxisOfSeries["humidity (min)"]);
        Assert.Equal("external_temperature, internal_temperature", curve.Axes.LeftLabel);
        Assert.Equal("humidity", curve.Axes.RightLabel);
        var plotAxes = PlotAxes.For(Pairs("external_temperature", "humidity", "internal_temperature"));
        Assert.Equal(plotAxes.Notice, curve.Axes.Notice);                       // none: no page-side rule (D3-1)
        Assert.Null(curve.Axes.Notice);
    }

    [Fact]
    public void A_wind_station_has_its_speed_on_the_left_and_no_right_label()
    {
        var curve = Drawn(Channel("speed"));

        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["speed (min)"]);
        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["speed (max)"]);
        Assert.Equal("speed", curve.Axes.LeftLabel);
        Assert.Equal("", curve.Axes.RightLabel);
        Assert.Null(curve.Axes.Notice);
    }

    [Fact]
    public void Axes_come_from_the_drawn_series_only()
    {
        var t = Channel("temperature"); var x = Channel("x_axis"); var y = Channel("y_axis");
        var row = Row(Week1, "I1", null, 0.3, 0.2);                             // the temperature has no reading
        var curve = Build(new[] { t, x, y }, Rows(row), Rows(row));

        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["x_axis (min)"]);
        Assert.Equal(PlotAxes.Left, curve.Axes.AxisOfSeries["y_axis (max)"]);
        Assert.False(curve.Axes.AxisOfSeries.ContainsKey("temperature (min)"));
        Assert.Equal("Axe de gauche partagé par x_axis, y_axis : une seule échelle pour ces canaux.", curve.Axes.Notice);
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_comma_in_a_channel_name_is_refused_and_named()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WeeklyMinMaxCurves.ChannelsOf(Type("Jauge", Channel("a,b"), Channel("c"))));
        Assert.Contains("a,b", ex.Message);
    }

    [Fact]
    public void Two_channels_of_one_name_are_refused_and_named()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WeeklyMinMaxCurves.ChannelsOf(Type("Jauge", Channel("x_axis"), Channel("x_axis"))));
        Assert.Contains("x_axis", ex.Message);
    }

    [Fact]
    public void A_type_given_without_its_channels_is_refused()
    {
        var type = Type("Inclinomètre", Channel("x_axis"));
        type.ChannelDataTypeElementTypes.Add(new ChannelDataTypeElementType
        {
            ChannelDataTypeElementTypeId = Guid.NewGuid(), ElementTypeId = type.ElementTypeId, ChannelDataTypeId = Guid.NewGuid(),
        });

        Assert.Throws<ArgumentException>(() => WeeklyMinMaxCurves.ChannelsOf(type));
    }

    [Fact]
    public void A_row_of_the_wrong_shape_is_refused()
    {
        var x = Channel("x_axis"); var y = Channel("y_axis");
        var good = Row(Week1, "I1", 0.1, 0.2);

        Assert.Throws<ArgumentException>(() => Build(new[] { x, y }, Rows(Row(Week1, "I1", 0.1)), Rows(good)));   // short
        Assert.Throws<ArgumentException>(() => Build(new[] { x, y }, Rows(good), Rows(Row(Week1, "I1", 0.1, 0.2, 0.3))));   // long
        Assert.Throws<ArgumentException>(() => Build(new[] { x, y }, Rows(good), Rows(new object[] { "2026-05-11", "I1", 0.1, 0.2 })));
        Assert.Throws<ArgumentException>(() => Build(new[] { x, y }, Rows(new object[] { Week1, "I1", "0.1", 0.2 }), Rows(good)));
    }

    [Fact]
    public async Task ReadAsync_refuses_no_plotted_channel_or_no_sensor_before_any_read()
    {
        var x = Channel("x_axis");
        var type = Type("Inclinomètre", x);
        var sensor = new Element { ElementId = Guid.NewGuid(), Name = "I1", ElementTypeId = type.ElementTypeId, ElementType = type };
        DateTime start = Week1, end = Week2;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            WeeklyMinMaxCurves.ReadAsync(new[] { sensor }, Array.Empty<ChannelDataType>(), start, end, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            WeeklyMinMaxCurves.ReadAsync(Array.Empty<Element>(), new[] { x }, start, end, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            WeeklyMinMaxCurves.ReadAsync(new[] { sensor }, new[] { Channel("direction") }, start, end, CancellationToken.None));
    }
}
