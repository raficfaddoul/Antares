using AliantWebS.Data;
using AliantWebS.Models;
using Xunit;

namespace Shared.Tests.Monitoring;

/// <summary>
/// The dashboard's top-right graph and plan colours for every sensor type with an alarm threshold (decisions 10 and 11
/// of the 2026-10-22 redesign, slice s2). One curve per (type, channel) rated on at least one sensor, each weekly value
/// the highest reading over its own sensor's threshold, the plan coloured by each sensor's worst channel, the gauges
/// shackles-only. The types and channels here have names no code knows ("Type Zeta", "voie_q"): a type joins by its
/// data alone. Whatever would break the graph (two curves of one name, a comma, an ambiguous channel) or stand in for a
/// read that did not happen is refused, never renamed, merged or emptied.
/// </summary>
public class AlarmRatioCurvesTests
{
    static readonly DateTime Week1 = new(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);   // Mondays
    static readonly DateTime Week2 = new(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);

    static ChannelDataType Channel(string name) => new() { ChannelDataTypeId = Guid.NewGuid(), Name = name };

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

    static Element Sensor(ElementType type, string name) =>
        new() { ElementId = Guid.NewGuid(), Name = name, ElementTypeId = type.ElementTypeId, ElementType = type };

    // The thresholds as the page reads them: "Seuil Alarme" values through the one parse loop.
    static Dictionary<Guid, Dictionary<string, double>> Thresholds(params (Element Sensor, string Value)[] rows) =>
        SensorChargeRatioHelper.ThresholdsByChannel(rows.Select(r => (r.Sensor.ElementId, (string?)r.Value)));

    static IReadOnlyList<AlarmCurve> Plan(IReadOnlyCollection<Element> sensors,
        Dictionary<Guid, Dictionary<string, double>> thresholds, List<(Guid SensorId, string Channel)>? lacking = null) =>
        AlarmRatioCurves.Plan(sensors, thresholds, lacking ?? new List<(Guid SensorId, string Channel)>());

    // An InfluxService.GetData row: [0] time, [1] sensor id, [2] the channel's value.
    static object[] Row(DateTime at, Element sensor, double? value) => new object?[] { at, sensor.ElementId.ToString(), value }!;

    static Dictionary<Guid, List<object[]>> Rows(params (ChannelDataType Channel, object[][] Rows)[] reads) =>
        reads.ToDictionary(r => r.Channel.ChannelDataTypeId, r => r.Rows.ToList());

    // ── Plan ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_type_joins_by_data_alone_one_curve_per_type_and_channel_with_a_threshold()
    {
        var q = Channel("voie_q"); var r = Channel("voie_r");
        var zeta = Type("Type Zeta", q, r);
        var eta = Type("Type Eta", q);
        var z1 = Sensor(zeta, "Z1"); var e1 = Sensor(eta, "E1");

        var curves = Plan(new[] { z1, e1 }, Thresholds((z1, "voie_q:5")));

        var curve = Assert.Single(curves);                                  // no curve for Eta, none for voie_r
        Assert.Equal("Type Zeta", curve.TypeName);
        Assert.Same(q, curve.Channel);
        Assert.Equal(new Dictionary<Guid, double> { [z1.ElementId] = 5 }, curve.Thresholds);
    }

    [Fact]
    public void Sensors_of_the_type_without_a_threshold_for_the_channel_are_left_out_and_counted()
    {
        var q = Channel("voie_q"); var r = Channel("voie_r");
        var zeta = Type("Type Zeta", q, r);
        var z1 = Sensor(zeta, "Z1"); var z2 = Sensor(zeta, "Z2"); var z3 = Sensor(zeta, "Z3");

        var curves = Plan(new[] { z1, z2, z3 }, Thresholds((z1, "voie_q:5;voie_r:1"), (z2, "voie_r:2")));

        var onQ = curves.Single(c => c.Channel == q);
        Assert.Equal(3, onQ.SensorsOfType);
        Assert.Equal(2, onQ.WithoutThreshold);                              // z2, rated on voie_r only, and z3
        var onR = curves.Single(c => c.Channel == r);
        Assert.Equal(3, onR.SensorsOfType);
        Assert.Equal(1, onR.WithoutThreshold);
    }

    [Fact]
    public void A_threshold_for_a_channel_its_type_does_not_have_draws_nothing_and_is_reported()
    {
        var extension = Channel("extension"); var temperature = Channel("temperature");
        var zeta = Type("Type Zeta", extension, temperature);
        var z1 = Sensor(zeta, "Z1");
        var lacking = new List<(Guid SensorId, string Channel)>();

        var curves = Plan(new[] { z1 }, Thresholds((z1, "weight:3")), lacking);

        Assert.Empty(curves);
        Assert.Equal(new[] { (z1.ElementId, "weight") }, lacking);
    }

    [Fact]
    public void A_curve_is_named_type_dash_channel_with_the_channel_s_spelling_and_ordered_by_type_then_channel()
    {
        var extension = Channel("extension"); var temperature = Channel("temperature");
        var zeta = Type("Zeta", extension, temperature);
        var alpha = Type("Alpha", temperature);
        var z1 = Sensor(zeta, "Z1"); var a1 = Sensor(alpha, "A1");

        var curves = Plan(new[] { z1, a1 },
            Thresholds((z1, "Temperature:60;Extension:2"), (a1, "temperature:50")));

        Assert.Equal(new[] { "Alpha - temperature", "Zeta - extension", "Zeta - temperature" }, curves.Select(c => c.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_type_without_a_name_is_shown_by_its_id_in_its_curve_and_by_TypeName(string blank)
    {
        // One rule for the curves and the page's log lines (DashBoardComponent2.TypeNameOf): never a blank type.
        var q = Channel("voie_q");
        var nameless = Type(blank, q);
        var n1 = Sensor(nameless, "N1");

        var curve = Assert.Single(Plan(new[] { n1 }, Thresholds((n1, "voie_q:5"))));

        Assert.Equal(nameless.ElementTypeId.ToString(), SensorTypeCurves.TypeName(nameless));
        Assert.Equal(SensorTypeCurves.TypeName(nameless), curve.TypeName);
        Assert.Equal($"{nameless.ElementTypeId} - voie_q", curve.Name);
        Assert.Equal("Type Zeta", SensorTypeCurves.TypeName(Type("Type Zeta", q)));
    }

    [Fact]
    public void Types_are_grouped_by_id_not_by_instance()
    {
        // AsNoTracking gives each sensor its own ElementType object.
        var q = Channel("voie_q");
        var zeta = Type("Type Zeta", q);
        var copy = Type("Type Zeta", q);
        copy.ElementTypeId = zeta.ElementTypeId;
        var z1 = Sensor(zeta, "Z1"); var z2 = Sensor(copy, "Z2");

        var curve = Assert.Single(Plan(new[] { z1, z2 }, Thresholds((z1, "voie_q:5"), (z2, "voie_q:4"))));
        Assert.Equal(2, curve.Thresholds.Count);
        Assert.Equal(2, curve.SensorsOfType);
    }

    public static IEnumerable<object[]> BrokenNames() => new[]
    {
        new object[] { "two types of one name", "Type Zeta - voie_q" },
        new object[] { "a comma", "Type, Zeta - voie_q" },
        new object[] { "an ambiguous channel", "Type Zeta" },
    };

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public void Curve_names_that_would_break_the_graph_are_refused(string what, string named)
    {
        var q = Channel("voie_q");
        List<Element> sensors;
        Dictionary<Guid, Dictionary<string, double>> thresholds;
        switch (what)
        {
            case "two types of one name":
            {
                var z1 = Sensor(Type("Type Zeta", q), "Z1");
                var z2 = Sensor(Type("Type Zeta", q), "Z2");               // another id, the same name
                sensors = new() { z1, z2 };
                thresholds = Thresholds((z1, "voie_q:5"), (z2, "voie_q:4"));
                break;
            }
            case "a comma":
            {
                var z1 = Sensor(Type("Type, Zeta", q), "Z1");
                sensors = new() { z1 };
                thresholds = Thresholds((z1, "voie_q:5"));
                break;
            }
            default:
            {
                var z1 = Sensor(Type("Type Zeta", q, Channel("VOIE_Q")), "Z1");   // two channels whatever their case
                sensors = new() { z1 };
                thresholds = Thresholds((z1, "voie_q:5"));
                break;
            }
        }

        var refused = Assert.Throws<InvalidOperationException>(() => Plan(sensors, thresholds));
        Assert.Contains(named, refused.Message);
    }

    [Fact]
    public void A_sensor_without_its_type_loaded_is_refused()
    {
        var z1 = Sensor(Type("Type Zeta", Channel("voie_q")), "Z1");
        z1.ElementType = null;

        Assert.Throws<ArgumentException>(() => Plan(new[] { z1 }, Thresholds((z1, "voie_q:5"))));
    }

    [Fact]
    public void No_threshold_gives_no_curve_not_an_error()
    {
        var z1 = Sensor(Type("Type Zeta", Channel("voie_q")), "Z1");

        Assert.Empty(Plan(new[] { z1 }, Thresholds()));
    }

    // ── Rate ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Each_week_is_the_highest_reading_over_its_own_sensor_s_threshold_and_names_that_sensor()
    {
        var q = Channel("voie_q");
        var zeta = Type("Type Zeta", q);
        var z1 = Sensor(zeta, "Z1"); var z2 = Sensor(zeta, "Z2");
        var curves = Plan(new[] { z1, z2 }, Thresholds((z1, "voie_q:10"), (z2, "voie_q:2")));

        var reading = AlarmRatioCurves.Rate(curves, Rows((q, new[] { Row(Week1, z1, 8), Row(Week1, z2, 1.8), Row(Week2, z1, 3) })));

        var peaks = Assert.Single(reading.Curves).Peaks;
        Assert.Equal(new[] { Week1, Week2 }, peaks.Select(p => p.Week));
        Assert.Equal(0.9, peaks[0].Ratio, 10);                              // 1.8 / 2 beats 8 / 10, a higher reading
        Assert.Equal(z2.ElementId, peaks[0].SensorId);
        Assert.Equal(z1.ElementId, peaks[1].SensorId);
    }

    [Fact]
    public void Two_types_sharing_a_channel_peak_on_their_own_sensors_only()
    {
        var temperature = Channel("temperature");
        var zeta = Type("Type Zeta", temperature); var eta = Type("Type Eta", temperature);
        var z1 = Sensor(zeta, "Z1"); var e1 = Sensor(eta, "E1");
        var curves = Plan(new[] { z1, e1 }, Thresholds((z1, "temperature:50"), (e1, "temperature:100")));

        // One read of the channel, two curves.
        var reading = AlarmRatioCurves.Rate(curves, Rows((temperature, new[] { Row(Week1, z1, 40), Row(Week1, e1, 90) })));

        var byName = reading.Curves.ToDictionary(c => c.Curve.Name);
        Assert.Equal(z1.ElementId, Assert.Single(byName["Type Zeta - temperature"].Peaks).SensorId);
        Assert.Equal(0.8, byName["Type Zeta - temperature"].Peaks[0].Ratio, 10);
        Assert.Equal(e1.ElementId, Assert.Single(byName["Type Eta - temperature"].Peaks).SensorId);
        Assert.Equal(0.9, byName["Type Eta - temperature"].Peaks[0].Ratio, 10);
    }

    [Fact]
    public void A_sensor_is_coloured_by_its_worst_channel()
    {
        var extension = Channel("extension"); var temperature = Channel("temperature");
        var zeta = Type("Type Zeta", extension, temperature);
        var z1 = Sensor(zeta, "Z1"); var unrated = Sensor(zeta, "Z2"); var unread = Sensor(zeta, "Z3");
        var curves = Plan(new[] { z1, unrated, unread },
            Thresholds((z1, "extension:2;temperature:50"), (unread, "extension:2")));

        var reading = AlarmRatioCurves.Rate(curves, Rows(
            (extension, new[] { Row(Week1, z1, 1.2), Row(Week1, unrated, 5) }),
            (temperature, new[] { Row(Week1, z1, 40), Row(Week2, unrated, 99) })));

        Assert.Equal(new[] { z1.ElementId }, reading.WorstRatioBySensor.Keys);   // no threshold, or no reading: absent
        Assert.Equal(0.8, reading.WorstRatioBySensor[z1.ElementId], 10);         // temperature 0.8 over extension 0.6
        Assert.Null(reading.Shackles);
    }

    [Fact]
    public void A_curve_with_no_reading_has_no_peak_and_is_kept()
    {
        var q = Channel("voie_q");
        var z1 = Sensor(Type("Type Zeta", q), "Z1");
        var curves = Plan(new[] { z1 }, Thresholds((z1, "voie_q:5")));

        var reading = AlarmRatioCurves.Rate(curves, Rows((q, Array.Empty<object[]>())));

        Assert.Empty(Assert.Single(reading.Curves).Peaks);
        Assert.Empty(reading.WorstRatioBySensor);
    }

    [Fact]
    public void A_curve_whose_channel_was_not_read_is_refused()
    {
        var q = Channel("voie_q"); var r = Channel("voie_r");
        var z1 = Sensor(Type("Type Zeta", q, r), "Z1");
        var curves = Plan(new[] { z1 }, Thresholds((z1, "voie_q:5;voie_r:1")));

        var refused = Assert.Throws<ArgumentException>(() => AlarmRatioCurves.Rate(curves, Rows((q, Array.Empty<object[]>()))));
        Assert.Contains("Type Zeta - voie_r", refused.Message);
    }

    [Fact]
    public void The_highest_shackle_curve_peak_is_the_max_gauge_and_other_types_leave_the_gauges_alone()
    {
        var weight = Channel("weight"); var extension = Channel("extension");
        var manille = Type("Manille", weight); var manille25 = Type("Manille 25 t", weight);
        var crack = Type("Type Zeta", extension);
        var m1 = Sensor(manille, "M1"); var m2 = Sensor(manille25, "M2"); var m3 = Sensor(manille, "M3");
        var z1 = Sensor(crack, "Z1");
        var thresholds = Thresholds((m1, "weight:1500"), (m2, "weight:1000"), (z1, "extension:1"));
        var curves = Plan(new[] { m1, m2, m3, z1 }, thresholds);
        var rows = Rows(
            (weight, new[] { Row(Week1, m1, 600), Row(Week1, m2, 900), Row(Week2, m1, 1650), Row(Week2, m3, 5000) }),
            (extension, new[] { Row(Week1, z1, 3.0) }));                      // 300 %: never a gauge

        var reading = AlarmRatioCurves.Rate(curves, rows);
        var shackleIds = new[] { m1.ElementId, m2.ElementId, m3.ElementId };
        var charge = SensorChargeRatioHelper.Charge(shackleIds, SensorChargeRatioHelper.ForChannel(thresholds, "weight"),
            new Dictionary<Guid, double> { [m1.ElementId] = 750, [m2.ElementId] = 500 }, rows[weight.ChannelDataTypeId]);

        var shackleCurves = reading.Curves.Where(c => c.Curve.Channel == weight).ToList();
        Assert.Equal(2, shackleCurves.Count);
        double highestShacklePeak = shackleCurves.SelectMany(c => c.Peaks).Max(p => p.Ratio);
        Assert.Equal(SensorChargeRatioHelper.Percent(highestShacklePeak), charge.Gauges!.MaxPercent);
        Assert.Equal(110, charge.Gauges.MaxPercent);                         // M1's 1650 / 1500
        Assert.Equal(3.0, reading.WorstRatioBySensor[z1.ElementId], 10);     // coloured on the plan, not in the gauges
        Assert.Equal(new ShackleGauges(MeanPercent: 50, MaxPercent: 110, CriticalPercent: 100), charge.Gauges);
        Assert.Equal(2, charge.Rated);
        Assert.Equal(1, charge.WithoutThreshold);
    }
}
