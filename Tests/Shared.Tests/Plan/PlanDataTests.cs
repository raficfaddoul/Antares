using AliantWebS.Models;
using AliantWebS.Plan;
using Xunit;

namespace Shared.Tests.Plan;

/// <summary>
/// Every input of the plan, in one frame before anything is drawn or exported.
///
/// The plan's inputs arrive in three frames - element ends and loads in metres from Point0, sensors in absolute
/// millimetres, charpente points in absolute metres - plus loads given as a distance along whatever carries them.
/// PlanData brings each into the stored frame (the grid's axes, metres from Point0), and the screen and the DXF
/// both read it from there. A load placed along the wrong end of its carrier, a sensor read in the wrong culture or
/// a point off by Point0 would be drawn AND exported wrong, so the rules are pinned here: a carried load is X metres
/// from the carrier's first end, a palonnier is looked up before a mother, sensor attributes are invariant culture.
/// </summary>
public class PlanDataTests
{
    private const double P0X = 33.103;   // E5, as at Antarès
    private const double Tolerance = 1e-9;

    private static MotherGridPalonnier El(string name, bool mother, double? x1, double? y1, double? x2, double? y2, Guid? id = null) => new()
    {
        MotherGridPalonnierId = id ?? Guid.NewGuid(), Famille = mother, Name = name, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
    };

    private static ChargeMp On(MotherGridPalonnier carrier, double? x, double? kg = 100) => new()
    {
        ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = carrier.MotherGridPalonnierId, Name = "L", X = x, Charge = kg,
    };

    private static AttributeElement Attr(string? type, string? value) => new()
    {
        AttributeElementId = Guid.NewGuid(), Value = value,
        AttributeType = type is null ? null! : new AttributeType { AttributeTypeId = Guid.NewGuid(), Name = type },
    };

    private static Element Sensor(params AttributeElement[] attributes)
    {
        var sensor = new Element { ElementId = Guid.NewGuid(), Name = "Manille" };
        foreach (var a in attributes) sensor.AttributeElements.Add(a);
        return sensor;
    }

    // ── Carried loads ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_load_on_a_palonnier_is_X_metres_along_it_from_its_first_end()
    {
        // Along beam G-F at x = -10,8, from line 3 (7,4) towards line 5 (0): 3 m along is y = 4,4.
        var palonnier = El("P300 L=8,0m #1", false, -10.8, 7.4, -10.8, 0);
        var data = new PlanData { Point0X = P0X, Palonniers = new[] { palonnier } };

        Assert.True(data.TryCarriedPosition(On(palonnier, 3.0), out var carrier, out var x, out var y),
            "la charge sur le palonnier doit être placée");
        Assert.Same(palonnier, carrier);
        Assert.True(data.IsPalonnier(carrier), "le porteur est un palonnier");
        Assert.True(Math.Abs(x - -10.8) <= Tolerance && Math.Abs(y - 4.4) <= Tolerance, $"position attendue (-10,8 ; 4,4), obtenue ({x} ; {y})");
    }

    [Fact]
    public void A_load_on_an_inclined_palonnier_follows_its_line()
    {
        // A 3-4-5 palonnier: 2,5 m along is half way.
        var palonnier = El("P incliné", false, 1, 2, 4, 6);
        var data = new PlanData { Palonniers = new[] { palonnier } };

        Assert.True(data.TryCarriedPosition(On(palonnier, 2.5), out _, out var x, out var y));
        Assert.True(Math.Abs(x - 2.5) <= Tolerance && Math.Abs(y - 4.0) <= Tolerance, $"position attendue (2,5 ; 4), obtenue ({x} ; {y})");
    }

    [Fact]
    public void A_load_hung_straight_on_a_mother_is_placed_along_the_mother()
    {
        // A motor within 8 cm of a point line hangs on the line itself: its ChargeMp names the mother.
        var mother = El("S500 L=14,4m #1", true, -14.406, 0, 0, 0);
        var data = new PlanData { Point0X = P0X, MotherGrids = new[] { mother } };

        Assert.True(data.TryCarriedPosition(On(mother, 5.0), out var carrier, out var x, out var y),
            "la charge accrochée directement à la mother doit être placée");
        Assert.Same(mother, carrier);
        Assert.False(data.IsPalonnier(carrier), "le porteur est une mother, pas un palonnier");
        Assert.True(Math.Abs(x - -9.406) <= Tolerance && Math.Abs(y) <= Tolerance, $"position attendue (-9,406 ; 0), obtenue ({x} ; {y})");
    }

    [Fact]
    public void A_carrier_of_zero_length_puts_its_load_on_its_first_end()
    {
        var palonnier = El("P zéro", false, 1.5, -2.5, 1.5, -2.5);
        var data = new PlanData { Palonniers = new[] { palonnier } };

        Assert.True(data.TryCarriedPosition(On(palonnier, 3.0), out _, out var x, out var y));
        Assert.Equal((1.5, -2.5), (x, y));
    }

    [Fact]
    public void A_load_without_X_is_not_placed()
    {
        var palonnier = El("P300", false, -10.8, 7.4, -10.8, 0);
        var data = new PlanData { Palonniers = new[] { palonnier } };

        Assert.False(data.TryCarriedPosition(On(palonnier, null), out var carrier, out _, out _),
            "une charge sans distance le long du porteur ne peut pas être placée");
        Assert.Same(palonnier, carrier);
    }

    [Fact]
    public void A_load_whose_carrier_is_unknown_or_incomplete_is_not_placed()
    {
        var incomplete = El("P sans X2", false, 2, 1, null, -3);
        var data = new PlanData { Palonniers = new[] { incomplete } };
        var orphan = new ChargeMp { ChargeMpid = Guid.NewGuid(), MotherGridPalonnierId = Guid.NewGuid(), X = 1, Charge = 100 };

        Assert.False(data.TryCarriedPosition(orphan, out var none, out _, out _), "une charge sans porteur ne peut pas être placée");
        Assert.Null(none);
        Assert.False(data.TryCarriedPosition(On(incomplete, 1), out var found, out _, out _),
            "une charge sur un porteur sans ses deux extrémités ne peut pas être placée");
        Assert.Same(incomplete, found);
    }

    [Fact]
    public void A_palonnier_is_looked_up_before_a_mother_of_the_same_id()
    {
        var id = Guid.NewGuid();
        var mother = El("Mother", true, -20, 3.7, 20, 3.7, id);
        var palonnier = El("Palonnier", false, 2, 11.05, 2, -11.05, id);
        var data = new PlanData { MotherGrids = new[] { mother }, Palonniers = new[] { palonnier } };

        Assert.Same(palonnier, data.CarrierOf(On(mother, 1)));
        Assert.True(data.TryCarriedPosition(On(mother, 1), out var carrier, out var x, out var y));
        Assert.Same(palonnier, carrier);
        Assert.Equal((2.0, 10.05), (x, Math.Round(y, 9)));
    }

    [Fact]
    public void An_element_has_ends_only_when_all_four_are_known()
    {
        Assert.True(PlanData.TryEnds(El("M", true, 1, 2, 3, 4), out var ends));
        Assert.Equal((1.0, 2.0, 3.0, 4.0), ends);

        Assert.False(PlanData.TryEnds(El("M", true, null, 2, 3, 4), out _), "X1 manquant");
        Assert.False(PlanData.TryEnds(El("M", true, 1, null, 3, 4), out _), "Y1 manquant");
        Assert.False(PlanData.TryEnds(El("M", true, 1, 2, null, 4), out _), "X2 manquant");
        Assert.False(PlanData.TryEnds(El("M", true, 1, 2, 3, null), out _), "Y2 manquant");
    }

    // ── Sensors ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_sensor_s_coordinates_are_read_in_the_invariant_culture()
    {
        Assert.True(PlanData.TrySensorAbsoluteMm(Sensor(Attr("X", "25947.5"), Attr("Y", "-3700")), out var x, out var y));
        Assert.Equal((25947.5, -3700.0), (x, y));

        Assert.True(PlanData.TrySensorAbsoluteMm(Sensor(Attr("Y", "-1.5E3"), Attr("X", " 1e4 ")), out x, out y),
            "l'exposant et les espaces autour sont admis, dans n'importe quel ordre d'attributs");
        Assert.Equal((10000.0, -1500.0), (x, y));

        Assert.False(PlanData.TrySensorAbsoluteMm(Sensor(Attr("X", "33103,5"), Attr("Y", "0")), out _, out _),
            "une virgule décimale n'est pas lue : les attributs sont en culture invariante");
    }

    [Fact]
    public void A_sensor_with_a_missing_or_unreadable_coordinate_has_no_position()
    {
        var data = new PlanData { Point0X = P0X };

        foreach (var sensor in new[]
                 {
                     Sensor(Attr("X", "33103")),
                     Sensor(Attr("Y", "0")),
                     Sensor(Attr("X", "abc"), Attr("Y", "0")),
                     Sensor(Attr("X", null), Attr("Y", "0")),
                     Sensor(Attr("X", ""), Attr("Y", "0")),
                     new Element { ElementId = Guid.NewGuid(), Name = "sans attributs", AttributeElements = null! },
                 })
        {
            Assert.False(PlanData.TrySensorAbsoluteMm(sensor, out _, out _),
                "un capteur sans X et Y lisibles ne doit pas avoir de coordonnées");
            Assert.False(data.TrySensorPosition(sensor, out _, out _), "ni de position sur le plan");
        }
    }

    [Fact]
    public void A_sensor_whose_coordinate_is_not_a_finite_number_has_no_position()
    {
        // The attribute is free text, and .NET reads these as numbers - NaN, or an infinity, whatever their case, and
        // an overflow as an infinity. Such a sensor used to count as positioned, and was drawn on Point0.
        var notFinite = new[] { "NaN", "Infinity", "-Infinity", "-NaN", "+Infinity", " infinity ", "nan", "1e309", "-1e309" };
        var data = new PlanData { Point0X = P0X };

        foreach (var value in notFinite)
        {
            Assert.True(double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                        && !double.IsFinite(parsed), $"« {value} » se lit bien comme un nombre non fini : le cas testé existe");

            foreach (var (what, sensor) in new[]
                     {
                         ("X", Sensor(Attr("X", value), Attr("Y", "0"))),
                         ("Y", Sensor(Attr("X", "33103"), Attr("Y", value))),
                         ("X et Y", Sensor(Attr("X", value), Attr("Y", value))),
                         ("X, Y illisible", Sensor(Attr("X", value), Attr("Y", "abc"))),
                     })
            {
                Assert.False(PlanData.TrySensorAbsoluteMm(sensor, out _, out _), $"{what} = « {value} » : le capteur n'a pas de coordonnées");
                Assert.False(data.TrySensorPosition(sensor, out _, out _), $"{what} = « {value} » : ni de position sur le plan");
            }
        }

        // Each pair of kinds, one finite and one not, both ways round.
        foreach (var (x, y) in new[] { ("NaN", "Infinity"), ("Infinity", "NaN"), ("-Infinity", "Infinity"), ("NaN", "NaN") })
            Assert.False(PlanData.TrySensorAbsoluteMm(Sensor(Attr("X", x), Attr("Y", y)), out _, out _), $"({x} ; {y})");
    }

    [Fact]
    public void A_very_large_finite_coordinate_is_still_a_position()
    {
        var data = new PlanData { Point0X = P0X };

        foreach (var (x, y) in new[] { ("1e308", "0"), ("0", "-1e308"), ("1.7976931348623157E+308", "-1.7976931348623157E+308"), ("4.9E-324", "-0") })
        {
            var sensor = Sensor(Attr("X", x), Attr("Y", y));
            Assert.True(PlanData.TrySensorAbsoluteMm(sensor, out var xMm, out var yMm), $"({x} ; {y}) est une position, si grande soit-elle");
            Assert.Equal(double.Parse(x, System.Globalization.CultureInfo.InvariantCulture), xMm);
            Assert.Equal(double.Parse(y, System.Globalization.CultureInfo.InvariantCulture), yMm);
            Assert.True(data.TrySensorPosition(sensor, out var sx, out var sy), $"({x} ; {y}) a une position sur le plan");
            Assert.True(double.IsFinite(sx) && double.IsFinite(sy));
        }
    }

    [Fact]
    public void An_attribute_without_a_type_is_ignored_and_the_first_X_wins()
    {
        var sensor = Sensor(Attr(null, "1"), Attr("X", "40247"), Attr("X", "999"), Attr("Y", "7400"));

        Assert.True(PlanData.TrySensorAbsoluteMm(sensor, out var x, out var y));
        Assert.Equal((40247.0, 7400.0), (x, y));
    }

    [Fact]
    public void A_sensor_s_position_is_its_millimetres_taken_from_Point0()
    {
        var data = new PlanData { Point0X = P0X, Point0Y = 0 };

        Assert.True(data.TrySensorPosition(Sensor(Attr("X", "25947"), Attr("Y", "-3700")), out var x, out var y));
        Assert.True(Math.Abs(x - (25.947 - 33.103)) <= Tolerance && Math.Abs(y - -3.7) <= Tolerance,
            $"F6 attendu en (-7,156 ; -3,7) depuis E5, obtenu ({x} ; {y})");
    }

    [Fact]
    public void A_sensor_s_status_and_ratio_are_read_when_known()
    {
        var sensor = Sensor(Attr("X", "0"), Attr("Y", "0"), Attr("Statut", "Panne"));
        var other = Sensor(Attr("X", "0"), Attr("Y", "0"));
        var data = new PlanData { SensorChargeRatios = new Dictionary<Guid, double> { [sensor.ElementId] = 0.75 } };

        Assert.Equal("Panne", PlanData.StatutOf(sensor));
        Assert.Null(PlanData.StatutOf(other));
        Assert.Equal(0.75, data.RatioOf(sensor));
        Assert.Null(data.RatioOf(other));
    }

    // ── Frames ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Absolute_millimetres_and_the_stored_frame_convert_back_and_forth_through_Point0()
    {
        var data = new PlanData { Point0X = P0X, Point0Y = -1.25 };

        var stored = data.AbsoluteMmToStored(25947, -3700);
        Assert.True(Math.Abs(stored.X - (25.947 - P0X)) <= Tolerance && Math.Abs(stored.Y - (-3.7 + 1.25)) <= Tolerance,
            $"repère stocké attendu ({25.947 - P0X} ; -2,45), obtenu {stored}");

        foreach (var (xMm, yMm) in new[] { (0.0, 0.0), (33103.0, 0.0), (62397.0, -15151.0), (-1234.5, 98765.4321) })
        {
            var (sx, sy) = data.AbsoluteMmToStored(xMm, yMm);
            var (bx, by) = data.StoredToAbsoluteMm(sx, sy);
            Assert.True(Math.Abs(bx - xMm) <= 1e-6 && Math.Abs(by - yMm) <= 1e-6,
                $"aller-retour de ({xMm} ; {yMm}) mm : obtenu ({bx} ; {by})");
        }
    }

    [Fact]
    public void E5_is_the_origin_when_Point0_is_E5()
    {
        var data = new PlanData { Point0X = P0X, Point0Y = 0 };

        Assert.True(data.TryCharpentePoint("E5", out var x, out var y));
        Assert.Equal((0.0, 0.0), (x, y));

        Assert.True(data.TryCharpentePoint("J1", out x, out y));
        Assert.True(Math.Abs(x - -33.103) <= Tolerance && Math.Abs(y - 15.15) <= Tolerance, $"J1 attendu en (-33,103 ; 15,15), obtenu ({x} ; {y})");

        Assert.False(data.TryCharpentePoint("Z9", out _, out _), "un point inconnu n'a pas de position");
    }

    // ── The venue load book ────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_load_book_s_capacities_are_read_by_name_leaving_out_what_is_empty()
    {
        var book = new Tcharpente
        {
            TcharpenteId = Guid.NewGuid(), Number = 7,
            A1 = 1500, A9 = 500, A10 = 0, C3 = 2500.5, E5 = 1500, E10 = 3000, J1 = -1, J9 = 500, J10 = 12000, D7 = 800,
            E6 = null, F5 = null,
            A1map = Guid.NewGuid(), E5map = Guid.NewGuid(), J10map = Guid.NewGuid(),
        };

        var capacities = PlanData.CapacitiesOf(book);

        // B4 is the one column the entity has as a plain double (NOT NULL): never empty, so always read - 0 here, as the
        // row holds it when nothing was typed.
        Assert.Equal(
            new Dictionary<string, double>
            {
                ["A1"] = 1500, ["A9"] = 500, ["A10"] = 0, ["C3"] = 2500.5, ["E5"] = 1500, ["E10"] = 3000, ["J1"] = -1, ["J9"] = 500,
                ["J10"] = 12000, ["D7"] = 800, ["B4"] = 0,
            }.OrderBy(kv => kv.Key),
            capacities.OrderBy(kv => kv.Key));
        Assert.DoesNotContain(capacities.Keys, k => k.EndsWith("map") || k is "Number" or "TcharpenteId");
    }

    [Fact]
    public void B4_a_plain_double_on_the_entity_is_read_like_the_points_beside_it()
    {
        // 2026-09-17 round 2: the scaffolded entity has B4 as double, not double? (the column is NOT NULL; the docker copy
        // holds 2 500 kg there, like B3 and B5). The Charpente page read it; the plan left it out, so a load on B4 was grey.
        Assert.Equal(typeof(double), typeof(Tcharpente).GetProperty("B4")!.PropertyType);

        var book = new Tcharpente { TcharpenteId = Guid.NewGuid(), B3 = 2500, B4 = 2500, B5 = 2500, B10 = 7000 };
        var capacities = PlanData.CapacitiesOf(book);

        Assert.Equal(new[] { ("B10", 7000.0), ("B3", 2500.0), ("B4", 2500.0), ("B5", 2500.0) },
            capacities.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(2500, new PlanData { CharpenteCapacities = capacities }.CapacityOf("B4"));

        // A load on B4 is then coloured by its ratio, like the same load on B3.
        var data = new PlanData
        {
            CharpenteCapacities = capacities,
            CharpenteLoads = new Dictionary<string, double> { ["B3"] = 2000, ["B4"] = 2000 },
        };
        Assert.Equal(PlanScene.CharpenteLoadStyle(data, "B3", 2000), PlanScene.CharpenteLoadStyle(data, "B4", 2000));
        Assert.Equal(("rgb(255,101,0)", false), PlanScene.CharpenteLoadStyle(data, "B4", 2000));
        Assert.StartsWith("Capacité du point : 2 500,00 kg — 80 %", PlanScene.CharpenteLoadTip(data, "B4", 2000).Split('\n')[1]);
    }

    [Fact]
    public void Every_double_or_nullable_double_named_like_a_point_or_a_beam_total_is_read_and_nothing_else()
    {
        // Every double and nullable double of the entity gets a value of its own, by reflection, and every map a Guid.
        var book = new Tcharpente { TcharpenteId = Guid.NewGuid(), Number = 3 };
        int n = 0;
        var expected = new Dictionary<string, double>();
        var plainDoubles = new List<string>();
        foreach (var property in typeof(Tcharpente).GetProperties())
        {
            if (property.PropertyType == typeof(double?) || property.PropertyType == typeof(double))
            {
                property.SetValue(book, (double)++n);
                expected[property.Name] = n;
                if (property.PropertyType == typeof(double)) plainDoubles.Add(property.Name);
            }
            else if (property.PropertyType == typeof(Guid?))
                property.SetValue(book, Guid.NewGuid());
        }
        Assert.Equal(100, expected.Count);   // A1 to J10
        Assert.All(expected.Keys, k => Assert.Matches("^[A-J](10|[1-9])$", k));
        Assert.Equal(new[] { "B4" }, plainDoubles);

        var capacities = PlanData.CapacitiesOf(book);

        foreach (var (name, value) in expected)
            Assert.True(capacities.TryGetValue(name, out var read) && read == value,
                $"{name} : attendu {value}, lu {(capacities.ContainsKey(name) ? read.ToString() : "rien")}");
        Assert.All(capacities.Keys, k => Assert.Matches("^[A-J](10|[1-9])$", k));
        Assert.Equal(expected.Count, capacities.Count);
    }

    [Fact]
    public void A_load_book_that_is_missing_or_empty_gives_no_capacity_on_record()
    {
        Assert.Empty(PlanData.CapacitiesOf(null));

        var empty = new PlanData
        {
            CharpenteCapacities = PlanData.CapacitiesOf(new Tcharpente { TcharpenteId = Guid.NewGuid(), A1map = Guid.NewGuid() }),
        };
        foreach (var name in CharpenteCoordinates.Points.Keys.Concat("ABCDEFGHIJ".Select(beam => beam + "10")))
            Assert.Null(empty.CapacityOf(name));
    }

    [Fact]
    public void A_capacity_is_on_record_only_above_zero()
    {
        var data = new PlanData
        {
            CharpenteCapacities = new Dictionary<string, double>
            {
                ["E5"] = 1500, ["E10"] = 3000, ["A1"] = 0, ["B2"] = -250, ["C3"] = 0.001, ["D4"] = double.NaN,
            },
        };

        Assert.Equal(1500, data.CapacityOf("E5"));
        Assert.Equal(3000, data.CapacityOf("E10"));
        Assert.Equal(0.001, data.CapacityOf("C3"));
        Assert.Null(data.CapacityOf("A1"));    // 0: not on record, as on the Charpente page
        Assert.Null(data.CapacityOf("B2"));    // negative: not a capacity
        Assert.Null(data.CapacityOf("D4"));
        Assert.Null(data.CapacityOf("F6"));    // missing
        Assert.Null(data.CapacityOf("e5"));    // names are case-sensitive
        Assert.Null(data.CapacityOf(null!));
        Assert.Null(new PlanData().CapacityOf("E5"));
    }

    [Fact]
    public void Every_charpente_point_is_listed_in_the_survey_s_order()
    {
        var data = new PlanData { Point0X = P0X };
        var points = data.CharpentePoints().ToList();

        Assert.Equal(CharpenteCoordinates.Points.Keys, points.Select(p => p.Key));
        var e5 = points.Single(p => p.Key == "E5");
        Assert.Equal((0.0, 0.0), (e5.X, e5.Y));
    }
}
